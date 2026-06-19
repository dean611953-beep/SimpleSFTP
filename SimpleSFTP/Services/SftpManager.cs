using Renci.SshNet;
using SimpleSFTP.Models;

namespace SimpleSFTP.Services
{
    /// <summary>
    /// SFTP客户端封装
    /// </summary>
    public class SftpManager : IDisposable
    {
        private SftpClient? _sftp;
        private bool _disposed = false;
        private string _currentPath = "/";

        public event Action<string>? StatusChanged;
        public event Action<List<RemoteFileInfo>>? FilesLoaded;
        public event Action<string>? ErrorOccurred;
        public event Action? ConnectedChanged;

        public bool IsConnected => _sftp?.IsConnected ?? false;
        public string CurrentPath => _currentPath;

        /// <summary>
        /// 连接SFTP服务器
        /// </summary>
        public void Connect(ServerInfo server)
        {
            try
            {
                StatusChanged?.Invoke($"正在连接到 {server.Host}...");
                
                var methods = new List<AuthenticationMethod>
                {
                    new PasswordAuthenticationMethod(server.Username, server.Password)
                };

                var connectionInfo = new ConnectionInfo(
                    server.Host,
                    server.Port,
                    server.Username,
                    methods.ToArray()
                );

                _sftp = new SftpClient(connectionInfo);
                _sftp.Connect();

                if (_sftp.IsConnected)
                {
                    _currentPath = "/";
                    StatusChanged?.Invoke("连接成功");
                    ConnectedChanged?.Invoke();
                }
                else
                {
                    throw new Exception("连接失败");
                }
            }
            catch (Exception ex)
            {
                Disconnect();
                ErrorOccurred?.Invoke($"连接失败: {ex.Message}");
                throw new Exception($"连接失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _sftp?.Disconnect();
                _sftp?.Dispose();
                _sftp = null;
                _currentPath = "/";
                StatusChanged?.Invoke("已断开连接");
                ConnectedChanged?.Invoke();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"断开连接出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 列出当前目录文件
        /// </summary>
        public List<RemoteFileInfo> ListFiles()
        {
            if (!IsConnected || _sftp == null)
                throw new InvalidOperationException("未连接到服务器");

            try
            {
                var entries = _sftp.ListEntries(_currentPath);
                var files = new List<RemoteFileInfo>();

                foreach (var entry in entries)
                {
                    if (entry.Name == "." || entry.Name == "..")
                        continue;

                    var fileInfo = new RemoteFileInfo
                    {
                        Name = entry.Name,
                        FullName = entry.FullName,
                        Size = entry.Length,
                        LastWriteTime = entry.LastWriteTime,
                        IsDirectory = entry.IsDirectory,
                        Permissions = entry.FileAttributes.ToString()
                    };
                    files.Add(fileInfo);
                }

                // 文件夹排在前面，然后按名称排序
                files.Sort((a, b) =>
                {
                    if (a.IsDirectory && !b.IsDirectory) return -1;
                    if (!a.IsDirectory && b.IsDirectory) return 1;
                    return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
                });

                FilesLoaded?.Invoke(files);
                return files;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"读取目录失败: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 进入子目录
        /// </summary>
        public bool EnterDirectory(string dirName)
        {
            var dir = _currentPath.TrimEnd('/');
            var newPath = string.IsNullOrEmpty(dir) || dir == "/" 
                ? "/" + dirName 
                : dir + "/" + dirName;
            
            try
            {
                if (_sftp?.Exists(newPath) == true)
                {
                    _currentPath = newPath;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 返回上级目录
        /// </summary>
        public bool GoToParent()
        {
            if (_currentPath == "/") return false;

            var path = _currentPath.TrimEnd('/');
            var lastSlash = path.LastIndexOf('/');
            
            if (lastSlash <= 0)
            {
                _currentPath = "/";
                return true;
            }

            _currentPath = path.Substring(0, lastSlash) == "" ? "/" : path.Substring(0, lastSlash);
            return true;
        }

        /// <summary>
        /// 下载单个文件
        /// </summary>
        /// <param name="remotePath">远程文件路径</param>
        /// <param name="localPath">本地保存路径</param>
        /// <param name="progressCallback">进度回调 (percent, downloaded, total)</param>
        public void DownloadFile(string remotePath, string localPath, Action<double, long, long> progressCallback)
        {
            if (!IsConnected || _sftp == null)
                throw new InvalidOperationException("未连接到服务器");

            try
            {
                // 确保目标目录存在
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var fileInfo = _sftp.GetEntry(remotePath);
                var totalSize = fileInfo.Length;

                using (var outputStream = File.OpenWrite(localPath))
                {
                    _sftp.DownloadFile(remotePath, outputStream);
                }

                progressCallback?.Invoke(100, totalSize, totalSize);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"下载失败 {Path.GetFileName(localPath)}: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 检查连接
        /// </summary>
        public bool CheckConnection()
        {
            return _sftp?.IsConnected ?? false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _sftp?.Disconnect();
                    _sftp?.Dispose();
                }
                catch { }
                _disposed = true;
            }
        }
    }
}
