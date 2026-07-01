@echo off
setlocal

set "APP_DIR=%~dp0StreamDoumi"
set "APP_PROJECT=%APP_DIR%\StreamDoumi.csproj"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%APP_DIR%"
dotnet run --project "%APP_PROJECT%"

pause
