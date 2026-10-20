@echo off
setlocal
cd /d "%~dp0"

set "PROJECT=PcMonitor.csproj"
set "OUT=%~dp0bin\Publish"
set "FW=%OUT%\PcMonitor-framework-dependent"
set "SA=%OUT%\PcMonitor-self-contained"

echo ==================================================
echo  PC Monitor - publish (win-x64)
echo  Output: %OUT%
echo ==================================================

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] dotnet SDK not found. Install .NET 8 SDK.
    goto :fail
)

if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%"

echo.
echo [1/2] Framework-dependent (needs .NET 8 Desktop Runtime)...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained false -o "%FW%"
if errorlevel 1 goto :fail

echo.
echo [2/2] Self-contained (no .NET required)...
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o "%SA%"
if errorlevel 1 goto :fail

echo.
echo Creating archives...
powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%FW%\*' -DestinationPath '%OUT%\PcMonitor-framework-dependent.zip' -Force; Compress-Archive -Path '%SA%\*' -DestinationPath '%OUT%\PcMonitor-self-contained.zip' -Force"
if errorlevel 1 goto :fail

echo.
echo ==================================================
echo  Done. Archives:
echo    %OUT%\PcMonitor-framework-dependent.zip
echo    %OUT%\PcMonitor-self-contained.zip
echo ==================================================
explorer "%OUT%"
endlocal
pause
exit /b 0

:fail
echo.
echo [FAILED] Publish was not completed. See messages above.
endlocal
pause
exit /b 1
