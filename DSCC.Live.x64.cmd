@echo off
setlocal
cd /d "%~dp0"

dotnet build ".\src\DSCC.App.Wpf\DSCC.App.Wpf.csproj" -p:Platform=x64
if errorlevel 1 (
    echo.
    echo DSCC x64 build failed.
    pause
    exit /b 1
)

start "" ".\src\DSCC.App.Wpf\bin\x64\Debug\net10.0-windows\DSCC.App.Wpf.exe" %*
