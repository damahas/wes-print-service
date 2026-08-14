@echo off
rem ============================================================
rem 卸载 Wes.PrintService Windows 服务
rem 用法：uninstall.bat [silent]
rem ============================================================
setlocal
set SERVICE_NAME=WesPrintService

sc query %SERVICE_NAME% >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    if not "%1"=="silent" echo 服务 %SERVICE_NAME% 不存在，无需卸载。
    exit /b 0
)

for /f "tokens=3" %%s in ('sc query %SERVICE_NAME% ^| findstr "STATE"') do set STATE=%%s
if not "%STATE%"=="STOPPED" (
    echo ==^> 停止服务 ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
)

echo ==^> 删除服务 [%SERVICE_NAME%] ...
sc delete %SERVICE_NAME%
if errorlevel 1 (
    echo sc delete 失败！
    exit /b 1
)
echo     服务已卸载
endlocal
