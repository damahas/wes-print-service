@echo off
rem ============================================================
rem Backup the published files to deploy\backups\publish_yyyyMMdd_HHmmss
rem and keep only the latest 5 backups.
rem ============================================================
setlocal
set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net10.0-windows\win-x64\publish"
set "BACKUP_ROOT=%~dp0backups"

if not exist "%PUBLISH_DIR%" (
    echo Publish directory not found: %PUBLISH_DIR%
    echo Run install.bat to generate the published files first.
    exit /b 1
)

for /f "tokens=1-3 delims=/ " %%a in ('date /t') do set D=%%a%%b%%c
for /f "tokens=1-2 delims=:." %%a in ("%time: =0%") do set T=%%a%%b
set "STAMP=%D%_%T%"
set "DEST=%BACKUP_ROOT%\publish_%STAMP%"

echo ==^> Backing up publish dir to %DEST% ...
robocopy "%PUBLISH_DIR%." "%DEST%" /E /NFL /NDL /NJH /NJS /NC /NS >nul
if errorlevel 8 (
    echo Backup failed.
    exit /b 1
)
echo     Backup done: %DEST%

set COUNT=0
for /f "delims=" %%d in ('dir /b /ad /o-d "%BACKUP_ROOT%\publish_*" 2^>nul') do (
    set /a COUNT+=1
    if !COUNT! gtr 5 (
        echo     Removing old backup: %%d
        rmdir /s /q "%BACKUP_ROOT%\%%d"
    )
)
endlocal
