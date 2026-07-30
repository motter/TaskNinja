@echo off
REM ─────────────────────────────────────────────────────────────────────
REM  Fix-TaskbarPin.cmd — repair the double-taskbar-icon issue
REM ─────────────────────────────────────────────────────────────────────
REM
REM  Why this script exists:
REM    Before v1.0.14, TaskNinja didn't set its AppUserModelID. When you
REM    pinned the .exe to the taskbar, Windows recorded the pin with an
REM    implicit ID based on the .exe path. Starting in v1.0.14 the
REM    running app declares "Anthropic.TaskNinja" as its AppID. Because
REM    the pinned shortcut still has the OLD implicit ID, Windows treats
REM    the pinned shortcut and the running app as TWO different apps,
REM    showing them as two separate taskbar buttons.
REM
REM  What this script does:
REM    Removes any existing TaskNinja pin from your taskbar, then guides
REM    you through re-pinning. Once you've re-pinned the RUNNING app
REM    (right-click its taskbar icon → Pin to taskbar), the new pin
REM    inherits the running app's AppID and they group correctly.
REM
REM  How to use:
REM    1. Double-click this script.
REM    2. Follow the on-screen instructions.
REM    3. After re-pinning, close and reopen TaskNinja — should be ONE
REM       icon now.
REM ─────────────────────────────────────────────────────────────────────

echo.
echo ===================================================================
echo   TaskNinja taskbar pin repair
echo ===================================================================
echo.
echo This script will help you fix the two-taskbar-icons issue.
echo.
echo Step 1: I'll remove any existing TaskNinja pin from your taskbar.
echo Step 2: Launch TaskNinja.
echo Step 3: Right-click its taskbar icon and choose "Pin to taskbar".
echo Step 4: Close and reopen TaskNinja - you should see ONE icon.
echo.
pause

REM Find the user's pinned taskbar folder
set "PINS=%APPDATA%\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
echo.
echo Looking for old pins in:
echo   %PINS%
echo.

REM Delete any TaskNinja-related .lnk files there
set DELETED=0
for %%F in ("%PINS%\TaskNinja.lnk" "%PINS%\TaskNinja*.lnk") do (
    if exist "%%F" (
        del "%%F"
        echo Removed: %%~nxF
        set /a DELETED+=1
    )
)

if %DELETED% EQU 0 (
    echo No existing TaskNinja pin found - you may not have pinned it before.
) else (
    echo Removed %DELETED% old pin file(s).
    echo.
    echo Restarting Explorer to refresh the taskbar...
    taskkill /f /im explorer.exe >nul 2>&1
    start explorer.exe
)

echo.
echo ===================================================================
echo   Next steps:
echo ===================================================================
echo   1. Launch TaskNinja.exe (or via Start menu)
echo   2. Right-click its icon on the taskbar
echo   3. Choose "Pin to taskbar"
echo   4. Close TaskNinja
echo   5. Click the pinned icon - it should open with ONLY ONE icon
echo ===================================================================
echo.
pause
