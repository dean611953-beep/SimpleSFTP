using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SimpleFTP.Models;

namespace SimpleFTP.Services
{
    public class FtpServer
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _running;
        private string _ipAddress = "";
        private readonly ConcurrentDictionary<string, List<UploadedFileInfo>> _uploadedFiles = new();
        private long _totalUploadedBytes;

        // Configuration
        public int Port { get; set; } = 21;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "";
        public string RootDirectory { get; set; } = "";
        public bool AnonymousAllowed { get; set; }
        public int MaxConnections { get; set; } = 10;

        // Events
        public Action<string>? LogMessage;
        public Action<string, string, long>? FileUploaded;
        public Action<long>? TotalUploadSizeChanged;

        public bool IsRunning => _running;
        public string IpAddress => _ipAddress;
        public IEnumerable<KeyValuePair<string, List<UploadedFileInfo>>> UploadedFiles => _uploadedFiles;
        public long TotalUploadedBytes => _totalUploadedBytes;

        public async Task StartAsync()
        {
            if (_running)
                throw new InvalidOperationException("Server is already running");

            // Auto-detect IP
            _ipAddress = GetLocalIpAddress() ?? "127.0.0.1";

            // Setup root directory
            if (string.IsNullOrWhiteSpace(RootDirectory))
            {
                RootDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "FTP_Uploads");
            }
            Directory.CreateDirectory(RootDirectory);

            // Start listening
            _listener = new TcpListener(new IPAddress(Array.Parse(_ipAddress.Replace(".", ","))), Port);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _running = true;

            LogMessage?.Invoke($"🚀 FTP Server started on {_ipAddress}:{Port}");
            LogMessage?.Invoke($"📁 Root directory: {RootDirectory}");
            LogMessage?.Invoke($"👤 Username: {Username}");

            // Accept connections loop
            try
            {
                while (_running && !_cts.Token.IsCancellationRequested)
                {
                    var socket = await _listener.AcceptSocketAsync();
                    _ = Task.Run(() => HandleClientAsync(socket), _cts.Token);
                }
            }
            catch (ObjectDisposedException)
            {
                // Expected during shutdown
            }
            finally
            {
                _running = false;
            }
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _listener?.Stop();
            LogMessage?.Invoke("⏹ FTP Server stopped");
        }

        private async Task HandleClientAsync(Socket socket)
        {
            var buffer = new byte[4096];
            var userName = "anonymous";
            var currentUser = "";
            var isLoggedIn = false;
            var currentDirectory = "/";
            var passivePort = 0;
            TcpListener? passiveListener = null;
            long? restOffset = null;
            var controlIp = GetClientIp(socket);

            try
            {
                while (socket.Connected && !_cts!.Token.IsCancellationRequested)
                {
                    var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0) break;

                    var commandLine = System.Text.Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim('\r', '\n');
                    if (string.IsNullOrEmpty(commandLine)) continue;

                    var parts = commandLine.Split(' ', 2);
                    var command = parts[0].ToUpper();
                    var arg = parts.Length > 1 ? parts[1] : "";

                    LogMessage?.Invoke($"<- {commandLine}");

                    switch (command)
                    {
                        case "QUIT":
                            SendResponse(socket, "221 Goodbye");
                            socket.Close();
                            return;

                        case "USER":
                            currentUser = arg.Trim();
                            if (string.IsNullOrEmpty(currentUser))
                            {
                                SendResponse(socket, "500 Username required");
                            }
                            else if (currentUser.Equals("anonymous", StringComparison.OrdinalIgnoreCase))
                            {
                                if (AnonymousAllowed)
                                    SendResponse(socket, "331 Password required for anonymous user");
                                else
                                    SendResponse(socket, "530 Anonymous access not allowed");
                            }
                            else
                            {
                                SendResponse(socket, "331 Password required");
                            }
                            break;

                        case "PASS":
                            if (currentUser.Equals(Username, StringComparison.OrdinalIgnoreCase) &&
                                arg.Equals(Password))
                            {
                                isLoggedIn = true;
                                userName = currentUser;
                                SendResponse(socket, "230 Login successful");
                                LogMessage?.Invoke($"✅ User '{userName}' logged in from {controlIp}");
                            }
                            else
                            {
                                SendResponse(socket, "530 Login incorrect");
                            }
                            break;

                        case "TYPE":
                            SendResponse(socket, "200 Type set to I");
                            break;

                        case "SYST":
                            SendResponse(socket, "215 UNIX Type: L8");
                            break;

                        case "NOOP":
                            SendResponse(socket, "200 NOOP ok");
                            break;

                        case "PWD":
                        case "XPWD":
                        case "CLNT":
                            SendResponse(socket, "257 \"&\"" + (command == "PWD" || command == "XPWD" ? " is current directory" : ""));
                            break;

                        case "PASV":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }

                            if (passiveListener != null)
                                passiveListener.Stop();

                            passiveListener = new TcpListener(IPAddress.Any, 0);
                            passiveListener.Start();
                            passivePort = (passiveListener.LocalEndpoint as IPEndPoint)!.Port;

                            var ipParts = _ipAddress.Split('.');
                            var portHigh = ((passivePort >> 8) & 0xFF).ToString();
                            var portLow = (passivePort & 0xFF).ToString();
                            SendResponse(socket, $"227 Entering Passive Mode ({string.Join(",", ipParts)},{portHigh},{portLow})");
                            LogMessage?.Invoke($"📡 PASV port: {passivePort}");
                            break;

                        case "PORT":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            // Parse PORT command (active mode - simplified)
                            SendResponse(socket, "200 PORT command successful");
                            break;

                        case "CWD":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var newDir = arg.Trim().Trim('/', '\\');
                            if (newDir.Equals(".."))
                            {
                                var parent = Path.GetDirectoryName(currentDirectory.Trim('/'));
                                currentDirectory = string.IsNullOrEmpty(parent) ? "/" : parent + "/";
                            }
                            else
                            {
                                currentDirectory = "/" + newDir + "/";
                            }
                            SendResponse(socket, $"250 Directory changed to {currentDirectory}");
                            break;

                        case "XPWD":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            SendResponse(socket, $"257 \"{currentDirectory}\" is current directory");
                            break;

                        case "LIST":
                        case "NLST":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            await HandleListCommand(socket, passiveListener!, currentDirectory);
                            break;

                        case "SIZE":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var sizePath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            if (File.Exists(sizePath))
                                SendResponse(socket, $"213 {new FileInfo(sizePath).Length}");
                            else
                                SendResponse(socket, "450 File not found");
                            break;

                        case "MKD":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var mkdPath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            Directory.CreateDirectory(mkdPath);
                            SendResponse(socket, "257 Directory created");
                            break;

                        case "RMD":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var rmPath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            if (Directory.Exists(rmPath))
                            {
                                Directory.Delete(rmPath);
                                SendResponse(socket, "250 Directory removed");
                            }
                            else
                                SendResponse(socket, "450 Directory not found");
                            break;

                        case "DELE":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var delPath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            if (File.Exists(delPath))
                            {
                                File.Delete(delPath);
                                SendResponse(socket, "250 File deleted");
                            }
                            else
                                SendResponse(socket, "450 File not found");
                            break;

                        case "RNFR":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            SendResponse(socket, "350 Ready for RNTO");
                            break;

                        case "RNTO":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            SendResponse(socket, "250 Rename successful");
                            break;

                        case "SITE MKDIR":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var siteMkdirPath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            Directory.CreateDirectory(siteMkdirPath);
                            SendResponse(socket, "257 Directory created");
                            break;

                        case "SITE RMDIR":
                        case "RDUP":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var siteRmdirPath = GetPhysicalPath(currentDirectory) + "/" + arg.Trim();
                            if (Directory.Exists(siteRmdirPath))
                                Directory.Delete(siteRmdirPath);
                            SendResponse(socket, "250 Directory removed");
                            break;

                        case "REST":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            if (long.TryParse(arg, out var offset))
                            {
                                restOffset = offset;
                                SendResponse(socket, $"212 Restarting at {offset}");
                            }
                            else
                                SendResponse(socket, "501 REST parameter must be a number");
                            break;

                        case "RETR":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            await HandleDownload(socket, passiveListener!, arg, currentDirectory, controlIp);
                            break;

                        case "STOR":
                            if (!isLoggedIn)
                            { SendResponse(socket, "530 Not logged in"); break; }
                            var storedOffset = restOffset;
                            restOffset = null;
                            await HandleUpload(socket, passiveListener!, arg, currentDirectory, userName, storedOffset, controlIp);
                            break;

                        default:
                            SendResponse(socket, $"502 Command '{command}' not implemented");
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                if (socket.Connected)
                    SendResponse(socket, $"451 Error: {ex.Message}");
            }
            finally
            {
                passiveListener?.Stop();
                if (socket.Connected)
                    socket.Close();
            }
        }

        private async Task HandleUpload(Socket controlSocket, TcpListener passiveListener, string filename, string currentDir, string user, long? restartOffset, string controlIp)
        {
            try
            {
                var dataSocket = await passiveListener.AcceptSocketAsync();
                var filePath = GetPhysicalPath(currentDir) + "/" + filename;
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                var buffer = new byte[65536];
                long totalBytes = 0;
                bool receivedAnyData = false;

                using var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None);
                if (restartOffset.HasValue && restartOffset.Value > 0)
                {
                    fs.Position = (long)restartOffset;
                    LogMessage?.Invoke($"🔄 Resumable transfer: {filename} from offset {(long)restartOffset}");
                }

                while (dataSocket.Connected && _cts!.Token.IsCancellationRequested == false)
                {
                    var bytesRead = await dataSocket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0) break;
                    fs.Write(buffer, 0, bytesRead);
                    totalBytes += bytesRead;
                    receivedAnyData = true;
                }

                dataSocket.Close();

                if (receivedAnyData)
                {
                    SendResponse(controlSocket, $"226 Transfer complete ({totalBytes} bytes)");

                    _uploadedFiles.AddOrUpdate(currentDir.Trim('/').Equals("") ? "root" : currentDir.Trim('/'),
                        k => new List<UploadedFileInfo> { new() {
                            FileName = filename, Size = totalBytes,
                            UploadedAt = DateTime.Now, IpAddress = controlIp, User = user, RelativePath = currentDir
                        }},
                        (k, list) => { list.Add(new UploadedFileInfo {
                            FileName = filename, Size = totalBytes,
                            UploadedAt = DateTime.Now, IpAddress = controlIp, User = user, RelativePath = currentDir
                        }); return list; });

                    _totalUploadedBytes += totalBytes;
                    TotalUploadSizeChanged?.Invoke(_totalUploadedBytes);

                    var offsetText = restartOffset.HasValue ? $" (resumed +{FormatBytes((long)restartOffset!)})" : "";
                    LogMessage?.Invoke($"✅ File uploaded: {filename} ({FormatBytes(totalBytes)}){offsetText} from {user}@{controlIp}");
                    FileUploaded?.Invoke(filename, user, totalBytes);
                }
            }
            catch (Exception ex)
            {
                SendResponse(controlSocket, $"451 Upload failed: {ex.Message}");
                LogMessage?.Invoke($"❌ Upload error: {ex.Message}");
            }
        }

        private async Task HandleDownload(Socket controlSocket, TcpListener passiveListener, string filename, string currentDir, string controlIp)
        {
            try
            {
                var dataSocket = await passiveListener.AcceptSocketAsync();
                var filePath = GetPhysicalPath(currentDir) + "/" + filename;

                if (!File.Exists(filePath))
                {
                    SendResponse(controlSocket, "450 File not found");
                    dataSocket.Close();
                    return;
                }

                SendResponse(controlSocket, $"150 Opening BINARY mode data connection for {filename}");

                var buffer = new byte[65536];
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                while (dataSocket.Connected && fs.Position < fs.Length)
                {
                    var bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                    await dataSocket.SendAsync(buffer.AsMemory(0, bytesRead), SocketFlags.None);
                }

                dataSocket.Close();
                SendResponse(controlSocket, "226 Transfer complete");
                LogMessage?.Invoke($"📤 Downloaded {filename} ({FormatBytes(fs.Length)}) to {controlIp}");
            }
            catch (Exception ex)
            {
                SendResponse(controlSocket, $"451 Download failed: {ex.Message}");
            }
        }

        private async Task HandleListCommand(Socket controlSocket, TcpListener passiveListener, string currentDir)
        {
            try
            {
                var dataSocket = await passiveListener.AcceptSocketAsync();
                var physicalPath = GetPhysicalPath(currentDir);

                if (!Directory.Exists(physicalPath))
                {
                    SendResponse(controlSocket, "550 Directory not found");
                    dataSocket.Close();
                    return;
                }

                var lines = new List<string>();
                foreach (var dir in Directory.GetDirectories(physicalPath))
                {
                    var name = Path.GetFileName(dir);
                    lines.Add($"d--------- 1 owner group      0 Jan 01 00:00 {name}");
                }
                foreach (var file in Directory.GetFiles(physicalPath))
                {
                    var name = Path.GetFileName(file);
                    var info = new FileInfo(file);
                    lines.Add($"---------- 1 owner group {info.Length,-10} Jan 01 00:00 {name}");
                }

                var response = string.Join("\r\n", lines) + "\r\n";
                var bytes = System.Text.Encoding.ASCII.GetBytes(response);
                await dataSocket.SendAsync(bytes, SocketFlags.None);

                dataSocket.Close();
                SendResponse(controlSocket, "226 Transfer complete");
            }
            catch (Exception ex)
            {
                SendResponse(controlSocket, $"450 List failed: {ex.Message}");
            }
        }

        private string GetPhysicalPath(string ftpPath)
        {
            var baseDir = RootDirectory;
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FTP_Uploads");

            var normalized = ftpPath.Trim('/', '\\');
            if (string.IsNullOrEmpty(normalized))
                return baseDir;

            return Path.Combine(baseDir, normalized);
        }

        private string? GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                        return ip.ToString();
                }
            }
            catch { }
            return null;
        }

        private string GetClientIp(Socket socket)
        {
            return socket.RemoteEndPoint?.ToString() ?? "unknown";
        }

        private void SendResponse(Socket socket, string response)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(response + "\r\n");
            try { socket.Send(bytes, SocketFlags.None); } catch { }
            LogMessage?.Invoke($"=> {response}");
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double b = bytes;
            for (int i = 0; i < sizes.Length; i++)
            {
                if (b <= 1024) return $"{b:F1} {sizes[i]}";
                b /= 1024;
            }
            return $"{b:F1} TB";
        }
    }
}
