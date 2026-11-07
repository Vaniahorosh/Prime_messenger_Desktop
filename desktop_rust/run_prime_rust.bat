@echo off
setlocal
cd /d "%~dp0"

if exist "%~dp0bin\release\prime-desktop.exe" (
    echo Starting Prime Desktop (Rust Native Client)...
    start "" "%~dp0bin\release\prime-desktop.exe"
    exit /b 0
)

if exist "%~dp0target\release\prime-desktop.exe" (
    echo Starting Prime Desktop (Rust Native Client)...
    start "" "%~dp0target\release\prime-desktop.exe"
    exit /b 0
)

echo [INFO] Release binary not found. Building now...
call "%~dp0build_rust.bat"

if exist "%~dp0target\release\prime-desktop.exe" (
    start "" "%~dp0target\release\prime-desktop.exe"
) else (
    echo [ERROR] Unable to start prime-desktop.exe
    pause
)
endlocal
