@echo off
rem ============================================================
rem 启动 / 停止 / 重启 / 查询 Wes.PrintService 服务
rem 用法：start-stop.bat start|stop|restart|status
rem ============================================================
setlocal
set SERVICE_NAME=WesPrintService

if "%1"=="" (
    echo 用法：%0 start^|stop^|restart^|status
    exit /b 1
)

if "%1"=="status" goto :status

sc query %SERVICE_NAME% >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    echo 服务 %SERVICE_NAME% 未安装，请先运行 install.bat
    exit /b 1
)

if "%1"=="start" (
    echo ==^> 启动服务 ...
    net start %SERVICE_NAME%
    goto :status
)
if "%1"=="stop" (
    echo ==^> 停止服务 ...
    net stop %SERVICE_NAME%
    goto :status
)
if "%1"=="restart" (
    echo ==^> 重启服务 ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
    net start %SERVICE_NAME%
    goto :status
)

echo 未知操作：%1
exit /b 1

:status
timeout /t 1 >nul
sc query %SERVICE_NAME%
endlocal
