@echo off
rem ============================================================
rem 回滚：用最近一次备份覆盖发布目录，并重启服务
rem 用法：rollback.bat [备份目录名]
rem   不传参则自动选最新备份
rem ============================================================
setlocal
set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net10.0\win-x64\publish"
set "BACKUP_ROOT=%~dp0backups"
set SERVICE_NAME=WesPrintService

rem 选择备份目录
if not "%1"=="" (
    set "SRC=%BACKUP_ROOT%\%1"
) else (
    for /f "delims=" %%d in ('dir /b /ad /o-d "%BACKUP_ROOT%\publish_*" 2^>nul') do (
        if not defined SRC set "SRC=%BACKUP_ROOT%\%%d"
    )
)

if not defined SRC (
    echo 没有找到任何备份，无法回滚。
    exit /b 1
)
if not exist "%SRC%" (
    echo 备份目录不存在：%SRC%
    exit /b 1
)

echo ==^> 回滚到备份：%SRC%
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
xcopy "%SRC%\*" "%PUBLISH_DIR%\" /E /I /Q /Y >nul
if errorlevel 1 (
    echo 回滚复制失败！
    exit /b 1
)

rem 重启服务使旧版本生效
sc query %SERVICE_NAME% >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    echo ==^> 重启服务 ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
    net start %SERVICE_NAME%
)
echo     回滚完成。
endlocal
