@echo off
rem Start test: server + shell + admin panel (ASCII only so cmd does not mix up the encoding)
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\test-all.ps1" -LocalZips "%~dp0zips" -Dir "%~dp0work"
echo.
echo Done. Close this window when finished. To stop everything run "STOP_ALL.cmd".
pause
