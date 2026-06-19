public namespace SimpleSFTP.Models
{
    /// <summary>
    /// 服务器连接信息
    /// </summary>
    public class ServerInfo
    {
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 22;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
        public DateTime LastConnected { get; set; } = DateTime.MinValue;
        public bool IsFavorite { get; set; } = false;
    }
}
