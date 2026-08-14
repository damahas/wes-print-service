@echo off
rem ============================================================
rem 安装 Wes.PrintService 为 Windows 服务并启动
rem 需以"管理员"身份运行
rem ============================================================
setlocal

set SERVICE_NAME=WesPrintService
set DISPLAY_NAME=Wes Print Service
set DESCRIPTION=Wes 打印服务（监听打印队列并调用打印机）
set RUNTIME=win-x64
set CONFIGURATION=Release

set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\%CONFIGURATION%\net10.0\%RUNTIME%\publish"
set "EXE_PATH=%PUBLISH_DIR%\Wes.PrintService.exe"

echo ==^> 发布项目 (%CONFIGURATION% / %RUNTIME%) ...
pushd "%PROJECT_DIR%"
dotnet publish -c %CONFIGURATION% -r %RUNTIME% --self-contained false
if errorlevel 1 (
    echo 发布失败！
    popd
    exit /b 1
)
popd
if not exist "%EXE_PATH%" (
    echo 发布失败：找不到 %EXE_PATH%
    exit /b 1
)
echo     发布成功：%EXE_PATH%

rem 若服务已存在则先卸载
sc query %SERVICE_NAME% >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo ==^> 检测到已存在的服务，先卸载...
    call "%~dp0uninstall.bat" silent
)

echo ==^> 注册 Windows 服务 [%SERVICE_NAME%] ...
sc create %SERVICE_NAME% binPath= "%EXE_PATH%" DisplayName= "%DISPLAY_NAME%" start= delayed-auto
if errorlevel 1 (
    echo sc create 失败！
    exit /b 1
)
sc description %SERVICE_NAME% "%DESCRIPTION%"
sc config %SERVICE_NAME% obj= "LocalSystem"

echo ==^> 启动服务 ...
net start %SERVICE_NAME%
echo 完成。查看日志：事件查看器 -^> 应用程序（来源 Wes.PrintService）
endlocal
