@echo off
rem ============================================================
rem Uninstall Wes.PrintService Windows service.
rem Usage: uninstall.bat [silent]
rem ============================================================
setlocal
set SERVICE_NAME=WesPrintService

sc query %SERVICE_NAME% >nul 2>&1
if not "%ERRORLEVEL%"=="0" (
    if not "%1"=="silent" echo Service %SERVICE_NAME% does not exist, nothing to uninstall.
    exit /b 0
)

for /f "tokens=3" %%s in ('sc query %SERVICE_NAME% ^| findstr "STATE"') do set STATE=%%s
if not "%STATE%"=="STOPPED" (
    echo ==^> Stopping service ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
)

echo ==^> Deleting service [%SERVICE_NAME%] ...
sc delete %SERVICE_NAME%
if errorlevel 1 (
    echo sc delete failed.
    exit /b 1
)
echo     Service uninstalled.
endlocal
