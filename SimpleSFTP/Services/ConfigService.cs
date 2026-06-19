using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace SimpleFTP.Services
{
    /// <summary>
    /// 本地配置服务 - 读写FTP服务器配置
    /// </summary>
    public static class ConfigService
    {
        private static readonly string ConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimpleFTP");

        private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

        public static string ConfigFilePath => ConfigFile;

        public static void EnsureConfigExists()
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);
        }

        public static Models.FtpConfig LoadConfig()
        {
            EnsureConfigExists();

            if (!File.Exists(ConfigFile))
                return new Models.FtpConfig();

            try
            {
                var json = File.ReadAllText(ConfigFile);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var config = JsonSerializer.Deserialize<FtpConfigData>(json, options);
                return config?.ToModel() ?? new Models.FtpConfig();
            }
            catch
            {
                return new Models.FtpConfig();
            }
        }

        public static void SaveConfig(Models.FtpConfig config)
        {
            EnsureConfigExists();

            var data = new FtpConfigData
            {
                Port = config.Port,
                Username = config.Username,
                Password = config.Password,
                RootDirectory = config.RootDirectory,
                AllowAnonymous = config.AllowAnonymous,
                MaxConnections = config.MaxConnections
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoding = System.Text.Encoding.UTF8
            };
            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(ConfigFile, json);
        }

        public static string GetLastUploadPath()
        {
            EnsureConfigExists();
            var pathFile = Path.Combine(ConfigDir, "uploadpath.txt");

            if (File.Exists(pathFile))
                return File.ReadAllText(pathFile).Trim();

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "FTP_Uploads");
        }

        public static void SetLastUploadPath(string path)
        {
            EnsureConfigExists();
            var pathFile = Path.Combine(ConfigDir, "uploadpath.txt");
            File.WriteAllText(pathFile, path);
        }

        // 内部配置类
        private class FtpConfigData
        {
            public int Port { get; set; }
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string RootDirectory { get; set; } = "";
            public bool AllowAnonymous { get; set; }
            public int MaxConnections { get; set; } = 10;

            public Models.FtpConfig ToModel()
            {
                return new Models.FtpConfig
                {
                    Port = Port,
                    Username = Username,
                    Password = Password,
                    RootDirectory = RootDirectory,
                    AllowAnonymous = AllowAnonymous,
                    MaxConnections = MaxConnections
                };
            }
        }
    }
}
