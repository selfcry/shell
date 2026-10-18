@echo off
rem Start test with power commands DISABLED: Reboot/Shutdown from admin only show a notice.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\test-all.ps1" -LocalZips "%~dp0zips" -Dir "%~dp0work"
echo.
echo Done. Close this window when finished. To stop everything run STOP_ALL.cmd.
pause
