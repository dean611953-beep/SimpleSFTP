@echo off
echo ================================
echo  SimpleFTP 发布脚本 (FTP服务端)
echo ================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo [错误] 未检测到 .NET SDK
    echo 请先安装 .NET 8.0 SDK
    pause
    exit /b 1
)

echo [*] 正在恢复 NuGet 包...
dotnet restore

if errorlevel 1 (
    echo [错误] NuGet 包恢复失败
    pause
    exit /b 1
)

echo [*] 正在编译...
dotnet build -c Release

if errorlevel 1 (
    echo [错误] 编译失败
    pause
    exit /b 1
)

echo [*] 正在发布...
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish

echo.
echo ================================
echo  发布完成!
echo  可执行文件: publish\SimpleFTP.exe
echo ================================
echo.
pause
