# SimpleSFTP - Windows 轻量 SFTP 客户端

专为**接收文件**设计的简单 SFTP 下载工具。

## 功能特点

- 一键连接 SFTP 服务器
- 浏览远程文件和文件夹
- 下载文件到本地
- 保存连接历史
- 进度条显示下载进度

## 系统要求

- **操作系统**: Windows 10/11 (64位)
- **.NET Runtime**: .NET 8.0 Runtime (首次运行自动下载)
- **协议**: SFTP (SSH File Transfer Protocol), 默认端口 22

## 使用方法

### 1. 连接服务器

1. 运行 `SimpleSFTP.exe`
2. 在左侧列表中选择历史服务器，或手动填写服务器信息
3. 点击"**连接**"按钮
4. 连接成功后，右侧会显示远程文件列表

### 2. 浏览文件

- **双击文件夹** 进入子目录
- 点击"**← 返回**" 回到上一级
- 点击"**🔄 刷新**" 更新文件列表

### 3. 下载文件

- **双击文件** 弹出保存对话框，选择保存位置
- 文件会自动下载到指定目录
- 底部状态栏显示实时下载进度

### 4. 管理连接历史

- 连接成功后会自动保存服务器信息到历史列表
- 右键点击历史列表中的服务器可重命名或删除
- 密码默认保存在本地配置文件中

## 配置文件位置

```
C:\Users\<用户名>\AppData\Roaming\SimpleSFTP\config.json
```

## 编译/运行 (开发者)

需要在 Windows 上安装 .NET 8.0 SDK:

```bash
# 安装 .NET 8 SDK (如果没有)
# 从 https://dotnet.microsoft.com/download/dotnet/8.0 下载

# 进入项目目录
cd SimpleSFTP

# 还原依赖
dotnet restore

# 编译
dotnet build -c Release

# 发布为单文件可执行程序
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ./publish
```

发布的 exe 位于 `SimpleSFTP/publish/SimpleSFTP.exe`

## 已知限制

- 只支持密码认证 (不支持密钥认证)
- 不支持上传、删除等写入操作 (本工具定位为纯下载客户端)
- 大文件下载不支持断点续传

## 许可证

MIT License

## 技术支持

如有问题，请检查是否安装了 .NET 8.0 Runtime。

---

*版本: 1.0.0 | 更新日期: 2026-06-19*
