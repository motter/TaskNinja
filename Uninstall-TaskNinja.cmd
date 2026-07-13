@echo off
REM TaskNinja uninstaller — removes the .exe and Start Menu shortcut.
REM Does NOT delete %AppData%\TaskNinja\ (your tasks and settings).

set "INSTALL_DIR=%LOCALAPPDATA%\Programs\TaskNinja"
set "START_DIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
set "DATA_DIR=%APPDATA%\TaskNinja"

echo Uninstalling TaskNinja ...

REM Stop any running instance
taskkill /IM TaskNinja.exe /F >nul 2>&1

REM Remove from HKCU Run if registered
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "TaskNinja" /f >nul 2>&1

REM Delete program files
if exist "%INSTALL_DIR%" rmdir /S /Q "%INSTALL_DIR%"

REM Delete Start Menu shortcut
if exist "%START_DIR%\TaskNinja.lnk" del "%START_DIR%\TaskNinja.lnk"

echo.
echo TaskNinja program files removed.
echo.
echo Your tasks and settings are preserved in: %DATA_DIR%
echo To fully wipe TaskNinja, manually delete that folder.
echo.
pause
