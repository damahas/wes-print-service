@echo off
rem ============================================================
rem Roll back the published files to a previous backup.
rem Usage: rollback.bat [backup-folder-name]
rem   If no name is given, the most recent backup is used.
rem ============================================================
setlocal
set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net10.0-windows\win-x64\publish"
set "BACKUP_ROOT=%~dp0backups"
set SERVICE_NAME=WesPrintService

if not "%1"=="" (
    set "SRC=%BACKUP_ROOT%\%1"
) else (
    for /f "delims=" %%d in ('dir /b /ad /o-d "%BACKUP_ROOT%\publish_*" 2^>nul') do (
        if not defined SRC set "SRC=%BACKUP_ROOT%\%%d"
    )
)

if not defined SRC (
    echo No backup found, cannot roll back.
    exit /b 1
)
if not exist "%SRC%" (
    echo Backup directory not found: %SRC%
    exit /b 1
)

echo ==^> Rolling back from %SRC%
robocopy "%SRC%." "%PUBLISH_DIR%." /E /NFL /NDL /NJH /NJS /NC /NS >nul
if errorlevel 8 (
    echo Rollback failed.
    exit /b 1
)

sc query %SERVICE_NAME% >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo ==^> Restarting service ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
    net start %SERVICE_NAME%
)
echo     Rollback done.
endlocal
