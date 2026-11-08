@echo off
echo ==============================================
echo   Building Prime Desktop (Windows x64)
echo   Unified C# / .NET 8 WPF + Rust Native Core
echo ==============================================

if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
if exist "%USERPROFILE%\.dotnet\dotnet.exe" set "PATH=%USERPROFILE%\.dotnet;%PATH%"
if exist "%USERPROFILE%\.cargo\bin\cargo.exe" set "PATH=%USERPROFILE%\.cargo\bin;%PATH%"

where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET 8 SDK not found in PATH.
    exit /b 1
)

set ROOT=%~dp0

if not exist "%ROOT%PrimeNative\Cargo.toml" goto :SKIP_RUST
where cargo >nul 2>nul
if errorlevel 1 goto :SKIP_RUST

echo.
echo [1/3] Building Rust Native Core (PrimeNative)...
cd /d "%ROOT%PrimeNative"
call cargo build --release
if exist "%ROOT%PrimeNative\target\release\prime_native.dll" (
    copy /y "%ROOT%PrimeNative\target\release\prime_native.dll" "%ROOT%PrimeDesktop\prime_native.dll" >nul
    echo [OK] prime_native.dll compiled and synced to PrimeDesktop.
)
:SKIP_RUST

cd /d "%ROOT%PrimeDesktop"
echo.
echo [2/3] Building PrimeDesktop Release (Portable)...
taskkill /F /IM PrimeDesktop.exe >nul 2>nul
taskkill /F /IM msedgewebview2.exe >nul 2>nul
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%ROOT%bin\Release"

if %ERRORLEVEL% neq 0 (
    echo [ERROR] PrimeDesktop build failed.
    exit /b 1
)

if not exist "%ROOT%bin\Portable" mkdir "%ROOT%bin\Portable"
copy /y "%ROOT%bin\Release\PrimeDesktop.exe" "%ROOT%bin\Portable\PrimeDesktop.exe" >nul

echo.
echo Cleaning runtime caches before packaging...
if exist "%ROOT%bin\Release\PrimeDesktop.exe.WebView2" rd /s /q "%ROOT%bin\Release\PrimeDesktop.exe.WebView2"

echo.
echo [3/3] Packaging application payload for installer...
if not exist "%ROOT%PrimeSetup\Payload" mkdir "%ROOT%PrimeSetup\Payload"
if exist "%ROOT%PrimeSetup\Payload\app_files.zip" del /f /q "%ROOT%PrimeSetup\Payload\app_files.zip"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%ROOT%bin\Release\*' -DestinationPath '%ROOT%PrimeSetup\Payload\app_files.zip' -Force"

echo Building PrimeSetup installer...
if exist "%ROOT%bin\Installer" rd /s /q "%ROOT%bin\Installer"
mkdir "%ROOT%bin\Installer"
cd /d "%ROOT%PrimeSetup"
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "%ROOT%bin\Installer"

if %ERRORLEVEL% equ 0 (
    echo.
    echo ==============================================
    echo [SUCCESS] Build completed successfully!
    echo Unified Hybrid Client - C# WPF and Rust Core
    echo Portable:  %ROOT%bin\Portable\PrimeDesktop.exe
    echo Installer: %ROOT%bin\Installer\PrimeSetup.exe
    echo ==============================================
) else (
    echo [ERROR] PrimeSetup build failed.
)
