@echo off
rem ============================================================
rem 查看 Wes.PrintService 的 Windows 事件日志
rem 用法：view-logs.bat [条数]
rem   默认显示最近 50 条，可传参如 view-logs.bat 100
rem ============================================================
setlocal
set SOURCE=Wes.PrintService
set COUNT=%1
if "%COUNT%"=="" set COUNT=50

echo ==^> 最近 %COUNT% 条事件（来源 %SOURCE%）...
wevtutil qe Application "/q:*[System[Provider[@Name='%SOURCE%']]]" /c:%COUNT% /rd:true /f:text
endlocal
