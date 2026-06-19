public namespace SimpleFTP.Models
{
    /// <summary>
    /// FTP服务器配置
    /// </summary>
    public class FtpConfig
    {
        public int Port { get; set; } = 21;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "";
        public string RootDirectory { get; set; } = "";
        public bool AllowAnonymous { get; set; } = false;
        public int MaxConnections { get; set; } = 10;
        public DateTime StartedAt { get; set; }
        public long TotalUploadedBytes { get; set; } = 0;
    }
}
