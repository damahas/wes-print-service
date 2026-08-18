@echo off
rem ============================================================
rem Install Wes.PrintService as a Windows service.
rem Run as Administrator.
rem ============================================================
setlocal

set SERVICE_NAME=WesPrintService
set DISPLAY_NAME=Wes Print Service
set DESCRIPTION=Wes Print Service (print queue, templates, and label printing)
set RUNTIME=win-x64
set CONFIGURATION=Release

set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\%CONFIGURATION%\net10.0-windows\%RUNTIME%\publish"
set "EXE_PATH=%PUBLISH_DIR%\Wes.PrintService.exe"
set "WWWROOT_DIR=%PUBLISH_DIR%\wwwroot"

echo ==^> Building project (%CONFIGURATION% / %RUNTIME%) ...
pushd "%PROJECT_DIR%"
dotnet publish -c %CONFIGURATION% -r %RUNTIME% --self-contained false
if errorlevel 1 (
    echo Build failed.
    popd
    exit /b 1
)
popd
if not exist "%EXE_PATH%" (
    echo Build failed: cannot find %EXE_PATH%
    exit /b 1
)
echo     Build succeeded: %EXE_PATH%
if not exist "%WWWROOT_DIR%\index.html" (
    echo Validation failed: cannot find %WWWROOT_DIR%\index.html
    exit /b 1
)
echo     Static web root: %WWWROOT_DIR%

rem Stop and remove existing service if present
sc query %SERVICE_NAME% >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo ==^> Existing service detected, uninstalling ...
    call "%~dp0uninstall.bat" silent
)

echo ==^> Registering Windows service [%SERVICE_NAME%] ...
sc create %SERVICE_NAME% binPath= "%EXE_PATH%" DisplayName= "%DISPLAY_NAME%" start= delayed-auto
if errorlevel 1 (
    echo sc create failed.
    exit /b 1
)
sc description %SERVICE_NAME% "%DESCRIPTION%"
sc config %SERVICE_NAME% obj= "LocalSystem"

echo ==^> Starting service ...
net start %SERVICE_NAME%
echo Done. Check logs: Event Viewer -^> Application -^> Source Wes.PrintService
endlocal
