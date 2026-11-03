@echo off
setlocal
cd /d "%~dp0"

echo ============================================================
echo  Gas Agency Manager - Create SINGLE portable .exe
echo ============================================================
echo.

dotnet publish GasAgencyManager.csproj -c Release -p:PublishSingleFile=true -p:EnableWindowsTargeting=true

if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    echo Install .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0
    pause
    exit /b 1
)

set SRC=bin\Release\net10.0-windows\win-x64\publish\GasAgencyManager.exe
set DIST=%~dp0GasAgencyManager-Portable

if not exist "%SRC%" (
    echo ERROR: Expected file not found:
    echo   %SRC%
    pause
    exit /b 1
)

if exist "%DIST%" rmdir /s /q "%DIST%"
mkdir "%DIST%"
copy /y "%SRC%" "%DIST%\GasAgencyManager.exe" >nul

echo.
echo SUCCESS - ONLY ONE FILE CREATED:
echo.
echo   %DIST%\GasAgencyManager.exe
echo.
echo IMPORTANT:
echo   - Copy ONLY GasAgencyManager.exe to your Desktop or any folder
echo   - You do NOT need any .dll files or cs/de/es folders
echo   - Those extra folders appear only if you copy the wrong build folder
echo   - AgencyDB.db is created automatically next to the .exe on first run
echo.
explorer "%DIST%"
pause


