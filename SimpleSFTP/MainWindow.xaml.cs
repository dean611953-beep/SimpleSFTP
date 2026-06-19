using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SimpleFTP.Models;
using SimpleFTP.Services;

namespace SimpleFTP
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private FtpServer _ftpServer = new();
        private ObservableCollection<FtpFileInfo> _uploadedFiles = new();
        private FtpConfig _config = new();

        // 用于XAML绑定的属性
        public string RootDirText
        {
            get => TxtRootDir.Text;
            set { TxtRootDir.Text = value; OnPropertyChanged(nameof(RootDirText)); }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // 加载配置
            _config = ConfigService.LoadConfig();
            TxtPort.Text = _config.Port.ToString();
            TxtUsername.Text = _config.Username;
            TxtPassword.Password = _config.Password;
            TxtRootDir.Text = _config.RootDirectory;

            if (string.IsNullOrWhiteSpace(TxtRootDir.Text))
            {
                TxtRootDir.Text = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "FTP_Uploads");
                _config.RootDirectory = TxtRootDir.Text;
            }

            // 绑定上传文件列表
            lstUploadedFiles.ItemsSource = _uploadedFiles;

            // 订阅FTP服务器事件
            _ftpServer.ServerStateChanged += OnServerStateChanged;
            _ftpServer.LogMessage += OnLogMessage;
            _ftpServer.UploadCompleted += OnUploadCompleted;
            _ftpServer.TotalUploadSizeChanged += OnTotalUploadSizeChanged;
        }

        #region 控制按钮

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var port = int.TryParse(TxtPort.Text, out var p) ? p : 21;
            var username = TxtUsername.Text.Trim() ?? "admin";
            var password = TxtPassword.Password ?? "";
            var rootDir = TxtRootDir.Text.Trim();

            if (string.IsNullOrWhiteSpace(rootDir))
            {
                MessageBox.Show("请指定保存目录", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show("请输入密码", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _ftpServer.Start(port, rootDir, username, password, _config.AllowAnonymous);
            _config.Port = port;
            _config.Username = username;
            _config.Password = password;
            _config.RootDirectory = rootDir;
            ConfigService.SaveConfig(_config);

            TxtPort.IsEnabled = false;
            TxtUsername.IsEnabled = false;
            TxtPassword.IsEnabled = false;
            TxtRootDir.IsEnabled = false;
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _ftpServer.Stop();
            ResetUI();
        }

        private void ResetUI()
        {
            TxtPort.IsEnabled = true;
            TxtUsername.IsEnabled = true;
            TxtPassword.IsEnabled = true;
            TxtRootDir.IsEnabled = true;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
        }

        #endregion

        #region 配置UI

        private void BtnBrowseDir_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = TxtRootDir.Text,
                Description = "选择FTP上传接收目录"
            };

            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtRootDir.Text = dlg.SelectedPath;
                ConfigService.SetLastUploadPath(dlg.SelectedPath);
            }
        }

        private void TxtPassword_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // 密码框使用PasswordBox，需要特殊处理
        }

        #endregion

        #region FTP服务器事件

        private void OnServerStateChanged(bool isRunning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isRunning)
                {
                    SetStatus("运行中", "#107C10");
                    TxtIp.Text = _ftpServer.IpAddress;
                    TxtConnections.Text = _ftpServer.ConnectedClients.ToString();
                }
                else
                {
                    SetStatus("已停止", "#CCCCCC");
                    TxtIp.Text = "-";
                    ResetUI();
                }
            });
        }

        private void OnLogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                TxtLog.Text += $"[{timestamp}] {message}\r\n";
                scrLog.ScrollToEnd();
            });
        }

        private void OnUploadCompleted(string fileName)
        {
            Dispatcher.Invoke(() => RefreshUploadedList());
        }

        private void OnTotalUploadSizeChanged(long totalBytes)
        {
            Dispatcher.Invoke(() =>
            {
                TxtTotalUpload.Text = FormatBytes(totalBytes);
            });
        }

        #endregion

        #region 文件列表刷新

        private void RefreshUploadedList()
        {
            _uploadedFiles.Clear();

            foreach (var kvp in _ftpServer.UploadedFiles)
            {
                foreach (var fileInfo in kvp.Value)
                {
                    _uploadedFiles.Add(new FtpFileInfo
                    {
                        FileName = fileInfo.RelativePath.TrimEnd('/') + "/" + fileInfo.FileName,
                        Size = fileInfo.Size,
                        UploadedAt = fileInfo.UploadedAt,
                        IpAddress = fileInfo.IpAddress,
                        User = fileInfo.User
                    });
                }
            }

            // 更新统计
            var totalFiles = _ftpServer.UploadedFiles.Values.Sum(list => list.Count);
            var totalDirs = _ftpServer.UploadedFiles.Count;

            if (totalFiles > 0)
            {
                var stats = $"共 {totalFiles} 个文件，来自 {totalDirs} 个目录\r\n总计: {FormatBytes(_ftpServer.TotalUploadedBytes)}";
                TxtUploadStats.Text = stats;
            }
            else
            {
                TxtUploadStats.Text = "暂无上传记录";
            }
        }

        #endregion

        #region 辅助方法

        private void SetStatus(string message, string hexColor)
        {
            TxtStatus.Text = message;
            try
            {
                var color = (System.Windows.Media.Brush)System.Windows.Markup.XamlReader.Parse(
                    $"<SolidColorBrush xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Color=\"{hexColor}\"/>");
                ElStatusDot.Fill = (System.Windows.Media.SolidColorBrush)color;
            }
            catch
            {
                ElStatusDot.Fill = System.Windows.Media.Brushes.Gray;
            }
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }

    /// <summary>
    /// 上传文件信息（用于XAML绑定）
    /// </summary>
    public class FtpFileInfo : INotifyPropertyChanged
    {
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public DateTime UploadedAt { get; set; }
        public string IpAddress { get; set; } = "";
        public string User { get; set; } = "";

        public string FormattedSize
        {
            get
            {
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
                return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
            }
        }

        public string UploadedAtStr
        {
            get
            {
                if (UploadedAt == DateTime.MinValue) return "-";
                return UploadedAt.ToString("MM-dd HH:mm");
            }
        }

        public string UserDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(User) || User == "unknown") return "-";
                return User;
            }
        }

        public string IpDisplay
        {
            get
            {
                if (string.IsNullOrEmpty(IpAddress)) return "-";
                // 只显示IP部分（去掉端口）
                var parts = IpAddress.Split(':');
                return parts.Length > 0 ? parts[0] : IpAddress;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
