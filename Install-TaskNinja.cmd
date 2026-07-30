@echo off
REM TaskNinja installer — copies the .exe to %LOCALAPPDATA%\Programs\TaskNinja
REM and creates a Start Menu shortcut. No admin rights needed.

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\TaskNinja"
set "START_DIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs"

echo Installing TaskNinja to %INSTALL_DIR% ...

if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

copy /Y "TaskNinja.exe" "%INSTALL_DIR%\TaskNinja.exe" >nul
if errorlevel 1 (
    echo Could not copy TaskNinja.exe. Make sure this script is in the same folder as the .exe.
    pause
    exit /b 1
)

REM Create Start Menu shortcut via PowerShell
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$s = (New-Object -ComObject WScript.Shell).CreateShortcut('%START_DIR%\TaskNinja.lnk'); $s.TargetPath = '%INSTALL_DIR%\TaskNinja.exe'; $s.WorkingDirectory = '%INSTALL_DIR%'; $s.Save()"

echo.
echo TaskNinja installed.
echo.
echo Launch from the Start Menu, or run: %INSTALL_DIR%\TaskNinja.exe
echo.
pause
