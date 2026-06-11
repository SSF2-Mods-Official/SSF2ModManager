@echo off
:: Kills all SSF2 Mod Manager processes for the current user (no admin required).
taskkill /IM SSF2ModManager.exe /F >nul 2>&1
if %errorlevel% equ 0 (
    echo All SSF2ModManager.exe processes have been closed.
) else (
    echo No SSF2ModManager.exe process was running.
)
