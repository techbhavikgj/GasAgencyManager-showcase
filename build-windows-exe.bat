@echo off
setlocal
cd /d "%~dp0"

echo Building Gas Agency Manager (single Windows .exe)...
echo.

dotnet publish GasAgencyManager.csproj -c Release -p:PublishSingleFile=true -p:EnableWindowsTargeting=true

if errorlevel 1 (
    echo.
    echo BUILD FAILED. Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

set OUT=bin\Release\net10.0-windows\win-x64\publish\GasAgencyManager.exe
echo.
echo SUCCESS!
echo.
echo Copy ONLY this one file:
echo   %CD%\%OUT%
echo.
echo Do NOT copy the bin folder with .dll files and cs/de/es language folders.
echo Run create-single-exe.bat to put a clean copy in GasAgencyManager-Portable\
echo.
pause


