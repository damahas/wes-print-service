@echo off
rem ============================================================
rem Manage the Wes.PrintService Windows service.
rem Usage (direct):  service.bat start|stop|restart|status
rem Double-click with no argument to use the interactive menu.
rem ============================================================
setlocal
set SERVICE_NAME=WesPrintService

if not "%1"=="" (
    if "%1"=="logs" (
        call :logs
        goto :done
    )
    call :action %1
    goto :done
)

:menu
cls
echo ===================== Wes Print Service =====================
echo  Current status:
sc query %SERVICE_NAME% >nul 2>&1
if "%ERRORLEVEL%"=="0" (
    for /f "tokens=*" %%s in ('sc query %SERVICE_NAME% ^| findstr "STATE"') do echo   %%s
) else (
    echo   [not installed]
)
echo -------------------------------------------------------------
echo  1) start  2) stop  3) restart  4) status  5) logs  0) exit
echo =============================================================
set /p CHOICE=Select [1-5/0]:
if "%CHOICE%"=="1" call :action start
if "%CHOICE%"=="2" call :action stop
if "%CHOICE%"=="3" call :action restart
if "%CHOICE%"=="4" call :action status
if "%CHOICE%"=="5" call :logs
if "%CHOICE%"=="0" goto :done
echo Invalid selection.
goto :menu

:action
set CMD=%1
sc query %SERVICE_NAME% >nul 2>&1
if "%CMD%"=="status" (
    sc query %SERVICE_NAME%
    goto :eof
)
if not "%ERRORLEVEL%"=="0" (
    if "%CMD%"=="start" (
        echo Service not installed. Run install.bat first.
    ) else (
        echo Service not installed.
    )
    goto :eof
)
if "%CMD%"=="start" (
    echo ==^> Starting service ...
    net start %SERVICE_NAME%
    goto :eof
)
if "%CMD%"=="stop" (
    echo ==^> Stopping service ...
    net stop %SERVICE_NAME%
    goto :eof
)
if "%CMD%"=="restart" (
    echo ==^> Restarting service ...
    net stop %SERVICE_NAME%
    timeout /t 2 >nul
    net start %SERVICE_NAME%
    goto :eof
)
echo Unknown command: %CMD%
goto :eof

:logs
wevtutil qe Application "/q:*[System[Provider[@Name='%SERVICE_NAME%']]]" /c:50 /rd:true /f:text
goto :eof

:done
echo.
echo Press any key to close ...
pause >nul
endlocal
