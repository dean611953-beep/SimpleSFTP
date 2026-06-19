using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SimpleFTP.Models
{
    public class FtpConfig : INotifyPropertyChanged
    {
        private int _port = 21;
        private string _username = "admin";
        private string _password = "";
        private string _rootDirectory = "";
        private bool _anonymousAllowed;
        private int _maxConnections = 10;

        public int Port
        {
            get => _port;
            set { _port = value; OnPropertyChanged(); }
        }

        public string Username
        {
            get => _username;
            set { _username = value; OnPropertyChanged(); }
        }

        public string Password
        {
            get => _password;
            set { _password = value; OnPropertyChanged(); }
        }

        public string RootDirectory
        {
            get => _rootDirectory;
            set { _rootDirectory = value; OnPropertyChanged(); }
        }

        public bool AllowAnonymous
        {
            get => _anonymousAllowed;
            set { _anonymousAllowed = value; OnPropertyChanged(); }
        }

        public int MaxConnections
        {
            get => _maxConnections;
            set { _maxConnections = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class UploadedFileInfo
    {
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public DateTime UploadedAt { get; set; }
        public string IpAddress { get; set; } = "";
        public string User { get; set; } = "";
        public string RelativePath { get; set; } = "";
    }
}
