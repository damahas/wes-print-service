@echo off
rem ============================================================
rem View Wes.PrintService Windows Event Log.
rem Usage: logs.bat [count]
rem   Default shows the latest 50 entries. e.g. logs.bat 100
rem ============================================================
setlocal
set SOURCE=Wes.PrintService
set COUNT=%1
if "%COUNT%"=="" set COUNT=50

echo ==^> Showing last %COUNT% events from source %SOURCE% ...
wevtutil qe Application "/q:*[System[Provider[@Name='%SOURCE%']]]" /c:%COUNT% /rd:true /f:text
echo.
echo Press any key to close ...
pause >nul
endlocal
