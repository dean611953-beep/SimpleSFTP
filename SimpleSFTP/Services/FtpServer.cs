using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace SimpleFTP.Services
{
    /// <summary>
    /// 简易FTP服务器实现 - 基于RFC 959
    /// </summary>
    public class FtpServer : IDisposable
    {
        private TcpListener _controlListener;
        private bool _isRunning = false;
        private int _port;
        private string _rootDirectory;
        private string _username;
        private string _password;
        private bool _anonymousAllowed;
        private ConcurrentDictionary<string, List<UploadedFileInfo>> _uploadedFiles;
        private List<Task> _connectionTasks = new();
        private CancellationTokenSource _cts;

        public event Action<bool>? ServerStateChanged; // true=running, false=stopped
        public event Action<string>? LogMessage; // 日志消息
        public event Action<string>? UploadCompleted; // 文件上传完成
        public event Action<long>? TotalUploadSizeChanged; // 总上传大小变化

        public int Port => _port;
        public string IpAddress { get; private set; } = "";
        public bool IsRunning => _isRunning;
        public int ConnectedClients => _connectionTasks.Count;
        public ConcurrentDictionary<string, List<UploadedFileInfo>> UploadedFiles => _uploadedFiles;
        public long TotalUploadedBytes { get; private set; }

        public class UploadedFileInfo
        {
            public string FileName { get; set; } = "";
            public long Size { get; set; }
            public DateTime UploadedAt { get; set; }
            public string IpAddress { get; set; } = "";
            public string User { get; set; } = "";
            public string RelativePath { get; set; } = "";
        }

        public FtpServer()
        {
            _uploadedFiles = new ConcurrentDictionary<string, List<UploadedFileInfo>>();
        }

        public void Start(int port, string rootDir, string username, string password, bool allowAnonymous = false)
        {
            _port = port;
            _rootDirectory = Path.GetFullPath(rootDir);
            _username = username;
            _password = password;
            _anonymousAllowed = allowAnonymous;

            if (!Directory.Exists(_rootDirectory))
                Directory.CreateDirectory(_rootDirectory);

            _uploadedFiles = new ConcurrentDictionary<string, List<UploadedFileInfo>>();
            _cts = new CancellationTokenSource();

            // 获取本机IP
            IpAddress = GetLocalIpAddress();

            _controlListener = new TcpListener(IPAddress.Any, port);
            _controlListener.Start();
            _isRunning = true;

            ServerStateChanged?.Invoke(true);
            LogMessage?.Invoke($"FTP服务已启动");
            LogMessage?.Invoke($"IP: {IpAddress}");
            LogMessage?.Invoke($"端口: {port}");
            LogMessage?.Invoke($"根目录: {_rootDirectory}");
            LogMessage?.Invoke($"用户: {(allowAnonymous ? "用户名/密码 或 匿名" : $"{username}/{password}")}");

            // 异步监听连接
            _ = ListenForConnectionsAsync(_cts.Token);
        }

        private async Task ListenForConnectionsAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _isRunning)
                {
                    var socket = await _controlListener.AcceptSocketAsync();
                    var tcs = new TaskCompletionSource<bool>();
                    _connectionTasks.Add(tcs.Task);

                    _ = HandleClientAsync(socket, tcs, ct);
                }
            }
            catch (ObjectDisposedException)
            {
                // 正常关闭时抛出
            }
            catch (Exception ex)
            {
                if (_isRunning)
                    LogMessage?.Invoke($"监听连接错误: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(Socket socket, TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            try
            {
                var buffer = new byte[4096];
                var userName = "unknown";
                var isLoggedIn = false;
                var currentDirectory = "/";
                var passiveMode = false;
                int? passivePort = null;

                // 发送欢迎消息
                SendResponse(socket, "220 Welcome to SimpleFTP Server");

                while (ct.IsCancellationRequested == false && socket.Connected)
                {
                    var bytesRead = await socket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0) break; // 客户端断开

                    var request = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var lines = request.Split('\r\n', StringSplitOptions.RemoveEmptyEntries);

                    foreach (var line in lines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        var parts = line.Split(' ', 2);
                        var command = parts[0].ToUpper();
                        var arg = parts.Length > 1 ? parts[1] : "";

                        switch (command)
                        {
                            case "USER":
                                if (string.IsNullOrEmpty(arg))
                                {
                                    SendResponse(socket, "500 Username required");
                                }
                                else if (arg.Equals("anonymous", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (_anonymousAllowed)
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
                                if (arg.Equals(_password, StringComparison.OrdinalIgnoreCase) ||
                                    (_anonymousAllowed && arg.Contains("@")))
                                {
                                    isLoggedIn = true;
                                    SendResponse(socket, "230 Login successful");
                                }
                                else
                                {
                                    SendResponse(socket, "530 Login incorrect");
                                }
                                break;

                            case "SYST":
                                SendResponse(socket, "215 UNIX Type: L8");
                                break;

                            case "FEAT":
                                SendResponse(socket, "211-Features:\r EPRT\r PASV\r REST STREAM\r SIZE\r TVFS\r211 End");
                                break;

                            case "TYPE":
                                SendResponse(socket, "200 Type set to I");
                                break;

                            case "PASV":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                var passiveListener = new TcpListener(IPAddress.Any, 0);
                                passiveListener.Start();
                                var endpoint = passiveListener.LocalEndpoint as IPEndPoint;
                                passivePort = endpoint.Port;
                                var ipParts = IpAddress.Split('.');
                                var portParts = ((ulong)passivePort >> 8).ToString() + "," + ((ulong)passivePort & 0xFF).ToString();
                                var pasvResponse = $"227 Entering Passive Mode ({string.Join(",", ipParts)},{portParts})";
                                SendResponse(socket, pasvResponse);
                                passiveMode = true;

                                // 存储passive listener用于后续数据传输
                                socket.AsyncState = passiveListener;
                                break;

                            case "LIST":
                            case "NLST":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                await HandleListCommand(socket, command, currentDirectory, passiveListener);
                                break;

                            case "CWD":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                if (string.IsNullOrEmpty(arg) || arg.Equals("."))
                                {
                                    currentDirectory = "/";
                                    SendResponse(socket, "250 Directory successfully changed");
                                }
                                else if (arg.Equals(".."))
                                {
                                    if (currentDirectory != "/")
                                    {
                                        var dirParts = currentDirectory.Trim('/').Split('/');
                                        if (dirParts.Length > 1)
                                            currentDirectory = "/" + string.Join("/", dirParts[..^1]) + "/";
                                        else
                                            currentDirectory = "/";
                                    }
                                    SendResponse(socket, "250 Directory successfully changed");
                                }
                                else
                                {
                                    var newPath = currentDirectory.TrimEnd('/') + "/" + arg.Trim('/');
                                    if (Directory.Exists(Path.Combine(GetPhysicalPath(currentDirectory), arg.Trim('/'))))
                                    {
                                        currentDirectory = newPath.EndsWith("/") ? newPath : newPath + "/";
                                        SendResponse(socket, "250 Directory successfully changed");
                                    }
                                    else
                                    {
                                        SendResponse(socket, "550 Directory not found");
                                    }
                                }
                                break;

                            case "PWD":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                SendResponse(socket, $"257 \"{currentDirectory}\" is current directory");
                                break;

                            case "DELE":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                // 不允许删除 - 这是接收端
                                SendResponse(socket, "550 Permission denied");
                                break;

                            case "QUIT":
                                SendResponse(socket, "221 Goodbye");
                                socket.Shutdown(SocketShutdown.Both);
                                socket.Close();
                                tcs.TrySetResult(true);
                                _connectionTasks.RemoveAll(t => t.IdenticalTo(tcs.Task));
                                return;

                            case "STRU":
                                SendResponse(socket, "200 FILE structure set");
                                break;

                            case "MODE":
                                SendResponse(socket, "200 PORT mode set");
                                break;

                            case "RETR":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                await HandleDownload(socket, arg, currentDirectory);
                                break;

                            case "STOR":
                                if (!isLoggedIn)
                                {
                                    SendResponse(socket, "530 Not logged in");
                                    break;
                                }
                                await HandleUpload(socket, arg, currentDirectory, userName, tcs, passiveListener);
                                break;

                            default:
                                SendResponse(socket, $"502 Command '{command}' not implemented");
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"客户端处理错误: {ex.Message}");
            }
            finally
            {
                tcs.TrySetResult(true);
                if (!_connectionTasks.RemoveAll(t => t.IdenticalTo(tcs.Task)))
                    _connectionTasks.Remove(tcs.Task);
            }
        }

        private string GetPhysicalPath(string virtualPath)
        {
            var path = _rootDirectory;
            if (!string.IsNullOrEmpty(virtualPath) && virtualPath != "/")
            {
                var relPath = virtualPath.Trim('/').Replace("/", "\\");
                path = Path.Combine(path, relPath);
            }
            return path;
        }

        private async Task HandleUpload(Socket clientSocket, string filename, string currentDir, string user, TaskCompletionSource<bool> tcs, CancellationToken ct)
        {
            try
            {
                // 等待被动模式数据连接
                if (clientSocket.AsyncState is TcpListener passiveListener)
                {
                    var dataSocket = await passiveListener.AcceptSocketAsync();
                    var filePath = Path.Combine(GetPhysicalPath(currentDir), filename);
                    var directory = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var buffer = new byte[8192];
                    long totalBytes = 0;
                    bool receivedSize = false;

                    // 检查REST命令(断点续传)
                    long restartPosition = 0;

                    using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.None))
                    {
                        if (restartPosition > 0)
                            fs.Seek(restartPosition, SeekOrigin.Begin);

                        while (ct.IsCancellationRequested == false && dataSocket.Connected)
                        {
                            var bytesRead = await dataSocket.ReceiveAsync(buffer, SocketFlags.None);
                            if (bytesRead == 0) break;

                            fs.Write(buffer, 0, bytesRead);
                            totalBytes += bytesRead;
                            receivedSize = true;
                        }
                    }

                    dataSocket.Close();

                    if (receivedSize)
                    {
                        SendResponse(clientSocket, $"226 Transfer complete ({totalBytes} bytes)");

                        // 记录上传文件
                        var fileInfo = new UploadedFileInfo
                        {
                            FileName = filename,
                            Size = totalBytes,
                            UploadedAt = DateTime.Now,
                            IpAddress = clientSocket.RemoteEndPoint?.ToString() ?? "",
                            User = user,
                            RelativePath = currentDir
                        };

                        var key = currentDir.Trim('/').Equals("") ? "root" : currentDir.Trim('/');
                        _uploadedFiles.AddOrUpdate(key,
                            k => new List<UploadedFileInfo> { fileInfo },
                            (k, list) => { list.Add(fileInfo); return list; });

                        TotalUploadedBytes += totalBytes;
                        TotalUploadSizeChanged?.Invoke(TotalUploadedBytes);

                        var displayName = string.IsNullOrEmpty(user) ? "unknown" : user;
                        LogMessage?.Invoke($"✅ 文件上传完成: {filename} ({FormatBytes(totalBytes)}) - 来自 {displayName}@{fileInfo.IpAddress}");
                        UploadCompleted?.Invoke(filename);
                    }
                }
                else
                {
                    SendResponse(clientSocket, "425 Use PASV or PORT first");
                }
            }
            catch (Exception ex)
            {
                SendResponse(clientSocket, $"451 Upload failed: {ex.Message}");
                LogMessage?.Invoke($"上传错误: {ex.Message}");
            }
        }

        private async Task HandleListCommand(Socket clientSocket, string command, string currentDir, TcpListener? passiveListener)
        {
            if (passiveListener == null)
            {
                SendResponse(clientSocket, "425 No data connection");
                return;
            }

            try
            {
                var dataSocket = await passiveListener.AcceptSocketAsync();
                var physicalPath = GetPhysicalPath(currentDir);

                var response = new StringBuilder();

                if (command == "LIST")
                {
                    // LIST格式: drwxr-xr-x 1 user group size date time name
                    foreach (var dir in Directory.EnumerateDirectories(physicalPath))
                    {
                        var name = Path.GetFileName(dir);
                        response.AppendLine($"drwxr-xr-x 1 user group 0 {DateTime.Now:MMM dd HH:mm} {name}/");
                    }

                    foreach (var file in Directory.EnumerateFiles(physicalPath))
                    {
                        var name = Path.GetFileName(file);
                        var info = new FileInfo(file);
                        response.AppendLine($"-rw-r--r-- 1 user group {info.Length} {info.LastWriteTime:MMM dd HH:mm} {name}");
                    }
                }
                else // NLST - 只列文件名
                {
                    foreach (var dir in Directory.EnumerateDirectories(physicalPath))
                        response.AppendLine(Path.GetFileName(dir) + "/");

                    foreach (var file in Directory.EnumerateFiles(physicalPath))
                        response.AppendLine(Path.GetFileName(file));
                }

                var responseData = Encoding.UTF8.GetBytes(response.ToString());
                await dataSocket.SendAsync(responseData, SocketFlags.None);
                dataSocket.Close();

                SendResponse(clientSocket, "226 Transfer complete");
            }
            catch (Exception ex)
            {
                SendResponse(clientSocket, $"451 List failed: {ex.Message}");
            }
        }

        private async Task HandleDownload(Socket clientSocket, string filename, string currentDir)
        {
            try
            {
                if (clientSocket.AsyncState is TcpListener passiveListener)
                {
                    var dataSocket = await passiveListener.AcceptSocketAsync();
                    var filePath = Path.Combine(GetPhysicalPath(currentDir), filename);

                    if (File.Exists(filePath))
                    {
                        var buffer = File.ReadAllBytes(filePath);
                        await dataSocket.SendAsync(buffer, SocketFlags.None);
                        dataSocket.Close();
                        SendResponse(clientSocket, $"226 Transfer complete ({buffer.Length} bytes)");
                    }
                    else
                    {
                        SendResponse(clientSocket, "450 File not found");
                        dataSocket.Close();
                    }
                }
                else
                {
                    SendResponse(clientSocket, "425 Use PASV first");
                }
            }
            catch (Exception ex)
            {
                SendResponse(clientSocket, $"451 Transfer failed: {ex.Message}");
            }
        }

        private void SendResponse(Socket socket, string message)
        {
            var response = Encoding.UTF8.GetBytes(message + "\r\n");
            socket.Send(response, SocketFlags.None);
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.ToString().StartsWith("127."))
                        return ip.ToString();
                }
                return host.AddressList[0]?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public void Stop()
        {
            _isRunning = false;
            _cts?.Cancel();

            try
            {
                _controlListener?.Stop();
            }
            catch { }

            ServerStateChanged?.Invoke(false);
            LogMessage?.Invoke("FTP服务已停止");
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }
    }
}
