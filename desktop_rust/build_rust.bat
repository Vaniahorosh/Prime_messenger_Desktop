@echo off
setlocal
echo ==============================================
echo   Building Prime Desktop (Rust Native Release)
echo ==============================================

set "MINGW=C:\Users\going\AppData\Local\Microsoft\WinGet\Packages\BrechtSanders.WinLibs.POSIX.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\mingw64\bin"
set "CARGO_BIN=%USERPROFILE%\.cargo\bin"
set "PATH=%MINGW%;%CARGO_BIN%;%PATH%"

cd /d "%~dp0"
echo Compiling optimized release binary...
cargo build --release

if %ERRORLEVEL% equ 0 (
    echo.
    echo ==============================================
    echo [SUCCESS] Release build finished successfully!
    echo Executable: %~dp0target\release\prime-desktop.exe
    echo ==============================================
) else (
    echo [ERROR] Compilation failed.
)
endlocal
pause
