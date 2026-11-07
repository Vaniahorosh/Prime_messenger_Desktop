@echo off
echo ==============================================
echo   Building Prime Desktop (Windows x64)
echo ==============================================

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
    set "PATH=%USERPROFILE%\.dotnet;%PATH%"
)

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET 8 SDK not found in PATH.
    echo Please install .NET 8 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

set ROOT=%~dp0
cd /d "%ROOT%PrimeDesktop"
echo Restoring packages and building PrimeDesktop Release...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%ROOT%bin\Release"

if %ERRORLEVEL% neq 0 (
    echo [ERROR] PrimeDesktop build failed.
    exit /b 1
)

echo.
echo Cleaning runtime caches before packaging...
if exist "%ROOT%bin\Release\PrimeDesktop.exe.WebView2" rd /s /q "%ROOT%bin\Release\PrimeDesktop.exe.WebView2"

echo Packaging application payload for installer...
if not exist "%ROOT%PrimeSetup\Payload" mkdir "%ROOT%PrimeSetup\Payload"
if exist "%ROOT%PrimeSetup\Payload\app_files.zip" del /f /q "%ROOT%PrimeSetup\Payload\app_files.zip"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%ROOT%bin\Release\*' -DestinationPath '%ROOT%PrimeSetup\Payload\app_files.zip' -Force"

echo.
echo Building PrimeSetup installer...
if exist "%ROOT%bin\Installer" rd /s /q "%ROOT%bin\Installer"
mkdir "%ROOT%bin\Installer"
cd /d "%ROOT%PrimeSetup"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "%ROOT%bin\Installer"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ==============================================
    echo [SUCCESS] Build completed!
    echo App: %ROOT%bin\Release\PrimeDesktop.exe
    echo Installer: %ROOT%bin\Installer\PrimeSetup.exe
    echo ==============================================
) else (
    echo [ERROR] PrimeSetup build failed.
)
