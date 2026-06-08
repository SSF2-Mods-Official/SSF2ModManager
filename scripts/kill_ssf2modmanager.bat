@echo off
:: Check for admin rights
openfiles >nul 2>&1
if %errorlevel% neq 0 (
    echo This script must be run as administrator!
    pause
    exit /b 1
)

taskkill /IM SSF2ModManager.exe /F

echo All SSF2ModManager.exe processes have been killed.
pause
