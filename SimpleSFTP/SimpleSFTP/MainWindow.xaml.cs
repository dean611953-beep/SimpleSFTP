using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using SimpleFTP.Models;
using SimpleFTP.Services;

namespace SimpleFTP
{
    public partial class MainWindow : Window
    {
        private readonly FtpServer _server = new();
        private FtpConfig? _config;

        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            UpdateStats();

            _server.LogMessage += msg => Dispatcher.Invoke(() =>
            {
                LogBox.AppendText($"{DateTime.Now:HH:mm:ss} {msg}\n");
                LogBox.ScrollToEnd();
            });

            _server.TotalUploadSizeChanged += bytes =>
                Dispatcher.Invoke(() => TotalBytesText.Text = FormatBytes(bytes));

            _server.FileUploaded += (fileName, user, size) =>
                Dispatcher.Invoke(() => UpdateFileList());
        }

        private void LoadConfig()
        {
            _config = ConfigService.LoadConfig();
            PortBox.Text = _config.Port.ToString();
            UserBox.Text = _config.Username;
            PassBox.Text = _config.Password;
            DirBox.Text = _config.RootDirectory;

            var ip = _server.IpAddress;
            if (!string.IsNullOrEmpty(ip))
                IpInfo.Text = $"服务器IP: {ip}  (客户端可通过此IP连接)";
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            _config ??= new FtpConfig();
            _config.Port = int.TryParse(PortBox.Text, out var port) ? port : 21;
            _config.Username = UserBox.Text.Trim();
            _config.Password = PassBox.Text;
            _config.RootDirectory = DirBox.Text.Trim();
            if (string.IsNullOrEmpty(_config.RootDirectory))
                _config.RootDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "FTP_Uploads");

            ConfigService.SaveConfig(_config);

            if (string.IsNullOrEmpty(_config.Username))
            { MessageBox.Show("请输入用户名！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (string.IsNullOrEmpty(_config.Password))
            { MessageBox.Show("请输入密码！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

            _server.Port = _config.Port;
            _server.Username = _config.Username;
            _server.Password = _config.Password;
            _server.RootDirectory = _config.RootDirectory;
            _server.AnonymousAllowed = _config.AnonymousAllowed;

            StartBtn.IsEnabled = false;
            StopBtn.IsEnabled = true;
            PortBox.IsEnabled = false;
            UserBox.IsEnabled = false;
            PassBox.IsEnabled = false;
            DirBox.IsEnabled = false;

            try
            {
                await _server.StartAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                ResetUI();
            }
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _server.Stop();
            ResetUI();
        }

        private void ResetUI()
        {
            StartBtn.IsEnabled = true;
            StopBtn.IsEnabled = false;
            PortBox.IsEnabled = true;
            UserBox.IsEnabled = true;
            PassBox.IsEnabled = true;
            DirBox.IsEnabled = true;
        }

        private void BrowseBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "选择文件夹";
            dlg.FileName = "*";
            dlg.Filter = "所有文件|*.*";
            if (dlg.ShowDialog() == true)
            {
                var dir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(dir))
                    DirBox.Text = dir;
            }
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            UpdateFileList();
        }

        private void UpdateFileList()
        {
            FileList.Items.Clear();
            foreach (var pair in _server.UploadedFiles)
            {
                foreach (var file in pair.Value)
                    FileList.Items.Add(file);
            }

            int count = 0;
            foreach (var pair in _server.UploadedFiles)
                count += pair.Value.Count;
            FileCountText.Text = count.ToString();
        }

        private void UpdateStats()
        {
            TotalBytesText.Text = FormatBytes(_server.TotalUploadedBytes);
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
