using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using SimpleFTP.Models;

namespace SimpleFTP.Services
{
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

        public static FtpConfig LoadConfig()
        {
            EnsureConfigExists();
            if (!File.Exists(ConfigFile))
                return new FtpConfig();

            try
            {
                var json = File.ReadAllText(ConfigFile);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var data = JsonSerializer.Deserialize<ConfigData>(json, options);
                return data?.ToConfig() ?? new FtpConfig();
            }
            catch
            {
                return new FtpConfig();
            }
        }

        public static void SaveConfig(FtpConfig config)
        {
            EnsureConfigExists();
            var data = new ConfigData
            {
                Port = config.Port,
                Username = config.Username,
                Password = config.Password,
                RootDirectory = config.RootDirectory,
                AllowAnonymous = config.AllowAnonymous,
                MaxConnections = config.MaxConnections
            };
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(data, options));
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
            File.WriteAllText(Path.Combine(ConfigDir, "uploadpath.txt"), path);
        }

        private class ConfigData
        {
            public int Port { get; set; }
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string RootDirectory { get; set; } = "";
            public bool AllowAnonymous { get; set; }
            public int MaxConnections { get; set; } = 10;

            public FtpConfig ToConfig() => new FtpConfig
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
