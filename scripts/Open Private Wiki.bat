@echo off
cd /d "%~dp0.."
echo Building the private wiki only...
powershell -NoProfile -ExecutionPolicy Bypass -File "wiki\_static\build-docs.ps1" -PrivateOnly
if errorlevel 1 (
  echo The private wiki did not build.
  pause
  exit /b 1
)
echo Opening the private pages. They stay on this PC.
start "" "%~dp0..\wiki\_private\index.html"
