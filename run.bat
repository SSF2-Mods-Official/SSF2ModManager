@echo off
cd /d "%~dp0"
dotnet build
if %errorlevel% neq 0 (
    echo Build failed.
    pause
    exit /b 1
)
start "" "bin\Debug\net8.0-windows\SSF2ModManager.exe"
