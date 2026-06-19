# MEMORY.md - 项目约定

## SimpleFTP 项目
- **路径**: `/Users/dean/WorkBuddy/2026-06-19-09-39-49/SimpleSFTP/`
- **GitHub**: https://github.com/dean611953-beep/SimpleSFTP
- **技术栈**: C# + WPF + .NET 8
- **用途**: Windows 轻量 FTP 服务端，让客户的电脑接收上传的文件
- **部署方式**: Windows 上运行 `publish.bat` 即可编译得到 exe
- **本地IP自动探测**: 不连接外网，扫描局域网IP段
- **核心特性**: PASV模式、断点续传、配置持久化

## GitHub 推送方式
- 仓库名: `SimpleSFTP`（虽然实际是FTP服务端，仓库名保持原名不改了）
- PAT token: dean611953-beep 的用户提供的 token
- 网络限制: macOS 虚拟机通过代理访问 GitHub 不稳定，需在宿主机或本地终端推送
