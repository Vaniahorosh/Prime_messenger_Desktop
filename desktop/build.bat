@echo off
echo ==============================================
echo   Building Prime Desktop (Windows x64)
echo ==============================================

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET 8 SDK not found.
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

cd /d "%~dp0\PrimeDesktop"
echo Restoring packages and building PrimeDesktop Release...
dotnet publish -c Release -r win-x64 --self-contained false -o ..\bin\Release

if %ERRORLEVEL% equ 0 (
    echo.
    echo Building PrimeSetup installer...
    cd /d "%~dp0\PrimeSetup"
    dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ..\bin\Installer
    echo.
    echo [SUCCESS] Build completed!
    echo App: %~dp0bin\Release\PrimeDesktop.exe
    echo Installer: %~dp0bin\Installer\PrimeSetup.exe
) else (
    echo [ERROR] Build failed.
)

pause
