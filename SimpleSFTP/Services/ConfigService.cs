using System.Text.Json;

namespace SimpleSFTP.Services
{
    /// <summary>
    /// 本地配置服务 - 读写服务器历史和下载设置
    /// </summary>
    public static class ConfigService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpleSFTP");
        
        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static string ConfigFilePath => ConfigFile;

        /// <summary>
        /// 确保配置目录存在
        /// </summary>
        public static void EnsureConfigExists()
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
        }

        /// <summary>
        /// 获取保存的服务器列表
        /// </summary>
        public static List<Models.ServerInfo> GetServers()
        {
            EnsureConfigExists();
            
            if (!File.Exists(ConfigFile))
                return new List<Models.ServerInfo>();

            try
            {
                var json = File.ReadAllText(ConfigFile);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var config = JsonSerializer.Deserialize<ConfigData>(json, options);
                return config?.Servers ?? new List<Models.ServerInfo>();
            }
            catch
            {
                return new List<Models.ServerInfo>();
            }
        }

        /// <summary>
        /// 保存服务器列表
        /// </summary>
        public static void SaveServers(List<Models.ServerInfo> servers)
        {
            EnsureConfigExists();
            
            var config = new ConfigData { Servers = servers };
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoding = System.Text.Encoding.UTF8
            };
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigFile, json);
        }

        /// <summary>
        /// 添加或更新服务器记录
        /// </summary>
        public static void AddOrUpdateServer(string name, string host, int port, string username, string password = "")
        {
            var servers = GetServers();
            
            // 查找是否已存在
            var existing = servers.FirstOrDefault(s => s.Host == host && s.Port == port && s.Username == username);
            
            if (existing != null)
            {
                existing.Name = name;
                existing.LastConnected = DateTime.Now;
                if (!string.IsNullOrEmpty(password))
                    existing.Password = password;
            }
            else
            {
                servers.Insert(0, new Models.ServerInfo
                {
                    Name = name,
                    Host = host,
                    Port = port,
                    Username = username,
                    Password = password,
                    LastConnected = DateTime.Now
                });
            }

            // 最多保留50个历史
            if (servers.Count > 50)
                servers.RemoveRange(50, servers.Count - 50);

            SaveServers(servers);
        }

        /// <summary>
        /// 删除服务器记录
        /// </summary>
        public static void RemoveServer(string host, int port, string username)
        {
            var servers = GetServers().Where(s => !(s.Host == host && s.Port == port && s.Username == username)).ToList();
            SaveServers(servers);
        }

        /// <summary>
        /// 设置默认下载路径
        /// </summary>
        public static string GetDefaultDownloadPath()
        {
            EnsureConfigExists();
            var downloadPathFile = Path.Combine(ConfigDir, "downloadpath.txt");
            
            if (File.Exists(downloadPathFile))
                return File.ReadAllText(downloadPathFile).Trim();
            
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        /// <summary>
        /// 保存默认下载路径
        /// </summary>
        public static void SetDefaultDownloadPath(string path)
        {
            EnsureConfigExists();
            var downloadPathFile = Path.Combine(ConfigDir, "downloadpath.txt");
            File.WriteAllText(downloadPathFile, path);
        }

        // 内部配置类
        private class ConfigData
        {
            public List<Models.ServerInfo> Servers { get; set; } = new();
        }
    }
}
