@echo off
setlocal
cd /d "%~dp0"

if exist "%~dp0desktop_rust\bin\release\prime-desktop.exe" (
    echo Starting Prime Desktop (Rust Native Client)...
    start "" "%~dp0desktop_rust\bin\release\prime-desktop.exe"
    exit /b 0
)

if exist "%~dp0desktop_rust\target\release\prime-desktop.exe" (
    echo Starting Prime Desktop (Rust Native Client)...
    start "" "%~dp0desktop_rust\target\release\prime-desktop.exe"
    exit /b 0
)

echo [INFO] Rust Release binary not found. Building now...
call "%~dp0desktop_rust\build_rust.bat"

if exist "%~dp0desktop_rust\bin\release\prime-desktop.exe" (
    start "" "%~dp0desktop_rust\bin\release\prime-desktop.exe"
) else if exist "%~dp0desktop_rust\target\release\prime-desktop.exe" (
    start "" "%~dp0desktop_rust\target\release\prime-desktop.exe"
) else (
    echo [ERROR] Unable to locate or run prime-desktop.exe
    pause
)
endlocal
