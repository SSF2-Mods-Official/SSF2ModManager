@echo off
setlocal EnableExtensions
cd /d "%~dp0\.."

:: Usage:
::   run.bat           Fast DEV sync into "SSF2 Mod Manager Release\" + start (default, double-click)
::   run.bat ship      Full self-contained single-file sync (what users get; slower)
::   run.bat sync      Alias for ship
::   run.bat publish   Full release zip via release\publish.ps1
::   run.bat bin       Framework build to src\bin only (no portable folder)

if exist "%ProgramFiles%\dotnet\dotnet.exe" set "PATH=%ProgramFiles%\dotnet;%PATH%"

if /I "%~1"=="ship" goto ship
if /I "%~1"=="sync" goto ship
if /I "%~1"=="release" goto ship
if /I "%~1"=="publish" goto publish
if /I "%~1"=="bin" goto bin
if /I "%~1"=="fast" goto bin
if not "%~1"=="" (
    echo Unknown argument: %~1
    echo Usage: run.bat ^| run.bat ship ^| run.bat publish ^| run.bat bin
    pause
    exit /b 1
)

:: --- default: fast portable-layout DEV sync ---
call :kill_app
echo.
echo DEV sync into "SSF2 Mod Manager Release\" (framework-dependent exe + runtime content)...
echo.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0release\sync-dev.ps1"
if errorlevel 1 (
    echo.
    echo ERROR: sync-dev.ps1 failed.
    echo Need .NET 8 SDK + desktop runtime: https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)
goto start_portable

:ship
call :kill_app
echo.
echo SHIP sync (self-contained single-file — slower)...
echo.
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0release\sync-release-exe.ps1"
if errorlevel 1 (
    echo.
    echo ERROR: sync-release-exe.ps1 failed.
    pause
    exit /b 1
)
goto start_portable

:start_portable
set "RELEASE=%~dp0..\SSF2 Mod Manager Release"
set "EXE=%RELEASE%\SSF2ModManager.exe"
if not exist "%EXE%" (
    echo Could not find "%EXE%"
    pause
    exit /b 1
)
echo.
echo Starting: %EXE%
start "" /D "%RELEASE%" "%EXE%"
exit /b 0

:bin
call :kill_app
echo Building framework-dependent Release to src\bin...
dotnet build src\SSF2ModManager.csproj -c Release
if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
)
set "EXE=%~dp0..\src\bin\Release\SSF2ModManager.exe"
if not exist "%EXE%" set "EXE=%~dp0..\src\bin\Release\win-x64\SSF2ModManager.exe"
if not exist "%EXE%" (
    echo Could not find SSF2ModManager.exe under src\bin\Release\
    pause
    exit /b 1
)
echo Starting: %EXE%
echo Tip: plain run.bat updates "SSF2 Mod Manager Release\" instead.
start "" "%EXE%"
exit /b 0

:publish
echo Running full publish pipeline (several minutes)...
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0release\publish.ps1"
if errorlevel 1 (
    echo publish.ps1 failed.
    pause
    exit /b 1
)
echo Done. Zip is under dist\
pause
exit /b 0

:kill_app
taskkill /IM SSF2ModManager.exe /F >nul 2>&1
if not errorlevel 1 (
    echo Closed existing SSF2 Mod Manager instance.
)
timeout /t 2 /nobreak >nul
goto :eof
