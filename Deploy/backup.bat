@echo off
rem ============================================================
rem 备份当前发布目录（用于更新前的快照 / 回滚）
rem 备份存放于 deploy\backups\publish_yyyyMMdd_HHmmss
rem ============================================================
setlocal
set "PROJECT_DIR=%~dp0..\Wes.PrintService"
set "PUBLISH_DIR=%PROJECT_DIR%\bin\Release\net10.0\win-x64\publish"
set "BACKUP_ROOT=%~dp0backups"

if not exist "%PUBLISH_DIR%" (
    echo 发布目录不存在：%PUBLISH_DIR%
    echo 请先运行 install.bat 生成发布文件。
    exit /b 1
)

for /f "tokens=1-3 delims=/ " %%a in ('date /t') do set D=%%a%%b%%c
for /f "tokens=1-2 delims=:." %%a in ("%time: =0%") do set T=%%a%%b
set "STAMP=%D%_%T%"
set "DEST=%BACKUP_ROOT%\publish_%STAMP%"

echo ==^> 备份发布目录到 %DEST% ...
xcopy "%PUBLISH_DIR%\*" "%DEST%\" /E /I /Q /Y >nul
if errorlevel 1 (
    echo 备份失败！
    exit /b 1
)
echo     备份完成：%DEST%

rem 仅保留最近 5 个备份
set COUNT=0
for /f "delims=" %%d in ('dir /b /ad /o-d "%BACKUP_ROOT%\publish_*" 2^>nul') do (
    set /a COUNT+=1
    if !COUNT! gtr 5 (
        echo     删除旧备份：%%d
        rmdir /s /q "%BACKUP_ROOT%\%%d"
    )
)
endlocal
