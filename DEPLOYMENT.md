# SimpleSFTP 部署指南

## 快速开始（三步完成）

### 第一步：安装 .NET 8.0 Runtime

如果你的 Windows 电脑还没有安装 .NET 8.0：

1. 下载地址：https://dotnet.microsoft.com/download/dotnet/8.0
2. 下载 **.NET 8.0 Runtime** (大约 20MB)
3. 安装完成后重启电脑

> 也可以只安装 **ASP.NET Core Runtime**，因为 WPF 应用需要 Runtime 包含 WPF 支持。

### 第二步：编译项目

方法 A — 使用发布脚本（推荐）：
```
1. 将本文件夹拷贝到 Windows 电脑
2. 双击运行 publish.bat
3. 等待编译完成
4. 生成的 exe 在 publish\SimpleSFTP.exe
```

方法 B — 使用命令行：
```powershell
cd SimpleSFTP
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o .\publish
```

### 第三步：运行

```
publish\SimpleSFTP.exe
```

---

## 常见问题

### Q: 双击 exe 没反应？
**A:** 检查是否安装了 .NET 8.0 Runtime。可以尝试在命令行运行 `.\SimpleSFTP.exe` 查看错误信息。

### Q: 连接超时怎么办？
**A:** 检查以下几点：
- FTP 服务器地址是否正确
- 端口是否为 22 (SFTP 默认端口)
- 防火墙是否放行了 22 端口
- 用户名和密码是否正确

### Q: 提示 "无法识别的 SSL/TLS 证书验证"
**A:** 某些 SFTP 服务器的 SSL 证书不被信任，这是正常的。联系服务器管理员确认证书，或暂时忽略此警告（需要在 SSH.NET 中添加 HostKey 事件处理）。

### Q: 下载大文件速度慢？
**A:** 这是服务器带宽限制导致的。工具本身使用了管道流传输，没有内存限制。

---

## 离线部署（给客户使用）

如果需要给客户分发，有两种方式：

**方式 1：附带运行时安装包**
1. 下载 .NET 8.0 Runtime Installer
2. 在客户电脑上先安装 Runtime
3. 再拷贝 SimpleSFTP.exe

**方式 2：自包含发布（推荐）**
修改 `publish.bat` 中的 `--self-contained false` 改为 `--self-contained true`，这样生成的 exe 自带运行时，但文件体积约 150MB。

---

*最后更新: 2026-06-19*
