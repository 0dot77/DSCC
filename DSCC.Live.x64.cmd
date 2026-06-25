@echo off
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo Requesting administrator privileges for Orbbec camera access...
    if "%~1"=="" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -WorkingDirectory '%~dp0' -Verb RunAs"
    ) else (
        powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -ArgumentList '%*' -WorkingDirectory '%~dp0' -Verb RunAs"
    )
    exit /b
)

set "APPDATA=%CD%\.appdata\Roaming"
set "LOCALAPPDATA=%CD%\.appdata\Local"
set "DOTNET_CLI_HOME=%CD%\.dotnet-cli-home"
set "DOTNET_ROOT=%CD%\.dotnet"
set "NUGET_PACKAGES=%CD%\.nuget\packages"
set "MSBUILDUSESERVER=0"
set "PATH=%CD%\.dotnet;%PATH%"

taskkill /IM DSCC.App.Wpf.exe /F >nul 2>&1
taskkill /IM dscc-k4abt-tracker.exe /F >nul 2>&1

".\.dotnet\dotnet.exe" msbuild ".\src\DSCC.App.Wpf\DSCC.App.Wpf.csproj" /t:Build /m:1 /p:Configuration=Debug /p:Platform=x64 /p:UseSharedCompilation=false /p:BuildInParallel=false /p:NodeReuse=false /v:minimal
if errorlevel 1 (
    echo.
    echo DSCC x64 build failed.
    pause
    exit /b 1
)

start "" ".\src\DSCC.App.Wpf\bin\x64\Debug\net10.0-windows\DSCC.App.Wpf.exe" --autostart %*
