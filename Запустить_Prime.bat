@echo off
setlocal
cd /d "%~dp0"

echo ==============================================
echo   Prime Messenger Desktop (C# .NET 8 WPF)
echo ==============================================

if exist "%~dp0desktop\bin\Release\PrimeDesktop.exe" (
    echo [INFO] Запуск Prime Desktop...
    start "" "%~dp0desktop\bin\Release\PrimeDesktop.exe"
    exit /b 0
)

if exist "%~dp0desktop\bin\Portable\PrimeDesktop.exe" (
    echo [INFO] Запуск Prime Desktop...
    start "" "%~dp0desktop\bin\Portable\PrimeDesktop.exe"
    exit /b 0
)

echo [INFO] Сборка приложения...
call "%~dp0desktop\build.bat"

if exist "%~dp0desktop\bin\Release\PrimeDesktop.exe" (
    start "" "%~dp0desktop\bin\Release\PrimeDesktop.exe"
) else (
    echo [ERROR] Не удалось запустить PrimeDesktop.exe
    pause
)
endlocal
