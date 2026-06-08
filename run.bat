@echo off
cd /d "%~dp0"
dotnet build -c Release
if %errorlevel% neq 0 (
    echo Build failed.
    pause
    exit /b 1
)
start "" "bin\Release\net8.0-windows\SSF2ModManager.exe"
