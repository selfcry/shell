@echo off
rem Stops server, shell and admin, and restores the Windows taskbar and Win key if the shell was closed by force.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\test-all.ps1" -Dir "%~dp0work" -Stop
pause
