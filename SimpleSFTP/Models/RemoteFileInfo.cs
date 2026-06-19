namespace SimpleSFTP.Models
{
    /// <summary>
    /// 远程文件信息
    /// </summary>
    public class RemoteFileInfo
    {
        public string Name { get; set; } = "";
        public string FullName { get; set; } = "";
        public long Size { get; set; } = 0;
        public DateTime LastWriteTime { get; set; }
        public bool IsDirectory { get; set; } = false;
        public string Permissions { get; set; } = "";

        /// <summary>
        /// 显示图标
        /// </summary>
        public string DisplayIcon
        {
            get
            {
                if (IsDirectory) return "📁";
                var ext = Name.Split('.').LastOrDefault()?.ToLowerInvariant();
                return ext switch
                {
                    "pdf" => "📕",
                    "doc" or "docx" => "📘",
                    "xls" or "xlsx" => "📗",
                    "ppt" or "pptx" => "📙",
                    "jpg" or "jpeg" or "png" or "gif" or "bmp" or "svg" => "🖼️",
                    "zip" or "rar" or "7z" or "tar" or "gz" => "📦",
                    "mp4" or "avi" or "mov" or "mkv" => "🎬",
                    "mp3" or "wav" or "flac" => "🎵",
                    "txt" or "log" => "📄",
                    "csv" or "json" or "xml" or "yaml" or "ini" => "📝",
                    _ => "📄"
                };
            }
        }

        /// <summary>
        /// 格式化修改时间
        /// </summary>
        public string LastWriteTimeStr
        {
            get
            {
                if (LastWriteTime == DateTime.MinValue) return "-";
                return LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        public string FormattedSize
        {
            get
            {
                if (IsDirectory) return "<DIR>";
                if (Size < 1024) return $"{Size} B";
                if (Size < 1024 * 1024) return $"{Size / 1024.0:F1} KB";
                if (Size < 1024 * 1024 * 1024) return $"{Size / (1024.0 * 1024):F1} MB";
                return $"{Size / (1024.0 * 1024 * 1024):F1} GB";
            }
        }
    }
}
