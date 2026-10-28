@echo off
rem ============================================================
rem Package the published output into zip archives.
rem   - framework.zip   : framework-dependent build (no runtime, needs .NET 10)
rem   - standalone.zip  : self-contained build (includes runtime)
rem Output: Deploy\publish\framework.zip, Deploy\publish\standalone.zip
rem Run from the repository root (where this Deploy folder lives) or anywhere.
rem ============================================================
setlocal

set "REPO_DIR=%~dp0.."
set "BUILD_ROOT=%REPO_DIR%\Wes.PrintService\bin\Release\net10.0-windows\win-x64"
set "SRC_FRAMEWORK=%BUILD_ROOT%\publish-framework"
set "SRC_STANDALONE=%BUILD_ROOT%\publish-standalone"
set "OUT_DIR=%~dp0publish"
set "CSPROJ=%REPO_DIR%\Wes.PrintService\Wes.PrintService.csproj"

for /f "usebackq tokens=*" %%v in (`powershell -NoProfile -Command "[xml](Get-Content '%CSPROJ%') | Select-Xml -XPath '//FileVersion' | ForEach-Object { $_.Node.InnerText }"`) do set "VERSION=%%v"

if "%VERSION%"=="" (
    echo Could not read version from %CSPROJ%
    exit /b 1
)

echo ==^> Packaging Wes.PrintService v%VERSION% build output ...

if not exist "%SRC_FRAMEWORK%\" (
    echo Missing framework build: %SRC_FRAMEWORK%
    echo   Run "Deploy\install.bat" or "dotnet publish --self-contained false" first.
    exit /b 1
)
if not exist "%SRC_STANDALONE%\" (
    echo Missing standalone build: %SRC_STANDALONE%
    echo   Run "dotnet publish --self-contained true" first.
    exit /b 1
)

if not exist "%OUT_DIR%\" mkdir "%OUT_DIR%"

rem Copy service scripts into build output so the unzipped package can be installed directly
rem (logs.bat is not shipped: service.bat already provides "service.bat logs")
echo     Copying service scripts into build output ...
for %%F in (install.bat uninstall.bat service.bat) do (
    copy /y "%~dp0%%F" "%SRC_FRAMEWORK%\" >nul
    if errorlevel 1 (
        echo Failed to copy %%F to %SRC_FRAMEWORK%
        exit /b 1
    )
    copy /y "%~dp0%%F" "%SRC_STANDALONE%\" >nul
    if errorlevel 1 (
        echo Failed to copy %%F to %SRC_STANDALONE%
        exit /b 1
    )
)

echo     Packing framework (no runtime) ...
powershell -NoProfile -Command "Compress-Archive -Force -Path '%SRC_FRAMEWORK%\*' -DestinationPath '%OUT_DIR%\WesPrint-framework-%VERSION%.zip'"
if errorlevel 1 (
    echo Failed to create WesPrint-framework-%VERSION%.zip
    exit /b 1
)

echo     Packing standalone (with runtime) ...
powershell -NoProfile -Command "Compress-Archive -Force -Path '%SRC_STANDALONE%\*' -DestinationPath '%OUT_DIR%\WesPrint-standalone-%VERSION%.zip'"
if errorlevel 1 (
    echo Failed to create WesPrint-standalone-%VERSION%.zip
    exit /b 1
)

echo.
echo ==^> Done. Archives created in: %OUT_DIR%
echo     WesPrint-framework-%VERSION%.zip   (framework-dependent, requires .NET 10 runtime)
echo     WesPrint-standalone-%VERSION%.zip  (self-contained, no runtime needed)
echo.
echo Press any key to close ...
pause >nul
endlocal
