@echo off
cd /d "%~dp0\.."

:: Close any running instance so the new build can start (dev workflow)
taskkill /IM SSF2ModManager.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo Closed existing SSF2 Mod Manager instance.
    timeout /t 1 /nobreak >nul
)

dotnet build src\SSF2ModManager.csproj -c Release
if %errorlevel% neq 0 (
    echo Build failed.
    pause
    exit /b 1
)
start "" "src\bin\Release\net8.0-windows\SSF2ModManager.exe"
