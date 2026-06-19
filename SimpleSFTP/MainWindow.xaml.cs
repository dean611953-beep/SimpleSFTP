using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using Renci.SshNet;
using SimpleSFTP.Models;
using SimpleSFTP.Services;

namespace SimpleSFTP
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private SftpManager _sftpManager;
        private ObservableCollection<RemoteFileInfo> _files;
        private bool _isConnecting = false;
        private ServerInfo? _selectedServer;

        public MainWindow()
        {
            InitializeComponent();
            
            _sftpManager = new SftpManager();
            _files = new ObservableCollection<RemoteFileInfo>();
            
            // 加载默认下载路径
            TxtDownloadPath.Text = ConfigService.GetDefaultDownloadPath();
            
            // 绑定文件列表
            lstFiles.ItemsSource = _files;
            
            // 加载历史服务器
            LoadServers();

            // 订阅事件
            _sftpManager.StatusChanged += OnStatusChanged;
            _sftpManager.FilesLoaded += OnFilesLoaded;
            _sftpManager.ErrorOccurred += OnErrorOccurred;
            _sftpManager.ConnectedChanged += OnConnectedChanged;
        }

        #region 服务器管理

        private void LoadServers()
        {
            var servers = ConfigService.GetServers();
            lstServers.ItemsSource = servers;
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedServer == null)
            {
                MessageBox.Show("请先选择一个服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ConnectToServer(_selectedServer);
        }

        private void LstServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstServers.SelectedItem is ServerInfo server)
            {
                _selectedServer = server;
            }
        }

        private void ConnectToServer(ServerInfo server)
        {
            if (_isConnecting) return;
            _isConnecting = true;

            SetStatus("正在连接...", "#FFB900");
            BtnConnect.IsEnabled = false;

            try
            {
                _sftpManager.Connect(server);
                
                ConfigService.AddOrUpdateServer(
                    server.Name, server.Host, server.Port, server.Username, server.Password);
                
                RefreshFileList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"连接失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("连接失败", "#D13438");
            }
            finally
            {
                _isConnecting = false;
                BtnConnect.IsEnabled = true;
            }
        }

        #endregion

        #region 连接管理

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _sftpManager.Disconnect();
            _files.Clear();
            lstServers.IsEnabled = true;
        }

        private void OnConnectedChanged()
        {
            Dispatcher.Invoke(() =>
            {
                if (_sftpManager.IsConnected)
                {
                    SetStatus("已连接", "#107C10");
                    TxtCurrentPath.Text = _sftpManager.CurrentPath;
                    lstServers.IsEnabled = false;
                    RefreshFileList();
                }
                else
                {
                    SetStatus("已断开", "#999999");
                    TxtCurrentPath.Text = "/";
                    lstServers.IsEnabled = true;
                }
            });
        }

        private void RefreshFileList()
        {
            try
            {
                _sftpManager.ListFiles();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载文件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 文件浏览

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_sftpManager.IsConnected)
            {
                RefreshFileList();
            }
            else
            {
                MessageBox.Show("请先连接服务器", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LstFiles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (lstFiles.SelectedItem is not RemoteFileInfo file || !_sftpManager.IsConnected) return;

            if (file.IsDirectory)
            {
                if (_sftpManager.EnterDirectory(file.Name))
                {
                    TxtCurrentPath.Text = _sftpManager.CurrentPath;
                    RefreshFileList();
                }
            }
            else
            {
                ShowSaveDialog(file);
            }
        }

        private void BtnGoBack_Click(object sender, RoutedEventArgs e)
        {
            if (_sftpManager.GoToParent())
            {
                TxtCurrentPath.Text = _sftpManager.CurrentPath;
                RefreshFileList();
            }
        }

        private void ShowSaveDialog(RemoteFileInfo file)
        {
            var saveDlg = new SaveFileDialog
            {
                FileName = file.Name,
                InitialDirectory = TxtDownloadPath.Text,
                Filter = "所有文件|*.*"
            };

            if (saveDlg.ShowDialog() == true)
            {
                StartDownload(file, saveDlg.FileName);
            }
        }

        #endregion

        #region 下载

        private void BtnBrowseDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = TxtDownloadPath.Text
            };
            
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TxtDownloadPath.Text = dlg.SelectedPath;
                ConfigService.SetDefaultDownloadPath(dlg.SelectedPath);
            }
        }

        private void TxtDownloadPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 用户手动修改了路径
        }

        private void StartDownload(RemoteFileInfo file, string localPath)
        {
            SetStatus($"正在下载: {file.Name}", "#0078D4");
            
            Task.Run(() =>
            {
                try
                {
                    _sftpManager.DownloadFile(file.FullName, localPath, (progress, downloaded, total) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.Value = progress;
                            ProgressBar.Visibility = Visibility.Visible;
                            TxtDownloadProgress.Text = $"⬇ {file.Name}: {FormatBytes(downloaded)}/{FormatBytes(total)} ({progress:F0}%)";
                        });
                    });

                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.Value = 0;
                        SetStatus("下载完成", "#107C10");
                        TxtDownloadProgress.Text = "";
                        MessageBox.Show($"下载完成！\n\n{localPath}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        ProgressBar.Visibility = Visibility.Collapsed;
                        ProgressBar.Value = 0;
                        SetStatus("下载失败", "#D13438");
                        TxtDownloadProgress.Text = "";
                        MessageBox.Show($"下载失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            });
        }

        #endregion

        #region 事件处理

        private void SetStatus(string message, string hexColor)
        {
            TxtStatus.Text = message;
            try
            {
                var brush = (Brush)System.Windows.Markup.XamlReader.Parse(
                    $"<SolidColorBrush xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Color=\"#{hexColor.TrimStart('#')}\"/>");
                ElStatusDot.Fill = (SolidColorBrush)brush;
            }
            catch
            {
                ElStatusDot.Fill = Brushes.Gray;
            }
        }

        private void OnStatusChanged(string message)
        {
            Dispatcher.Invoke(() => SetStatus(message, "#0078D4"));
        }

        private void OnFilesLoaded(List<RemoteFileInfo> files)
        {
            Dispatcher.Invoke(() =>
            {
                _files.Clear();
                foreach (var f in files)
                {
                    _files.Add(f);
                }
            });
        }

        private void OnErrorOccurred(string error)
        {
            Dispatcher.Invoke(() =>
            {
                SetStatus(error, "#D13438");
                TxtDownloadProgress.Text = "";
            });
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _sftpManager.Disconnect();
            _sftpManager.Dispose();
        }

        #endregion

        #region 工具方法

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion
    }
}
