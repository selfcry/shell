@echo off
rem Start test with power commands ENABLED: admin buttons Reboot/Shutdown really reboot/shut down THIS computer. Use only on a virtual machine.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\test-all.ps1" -LocalZips "%~dp0zips" -Dir "%~dp0work" -AllowPower
echo.
echo Done. Close this window when finished. To stop everything run STOP_ALL.cmd.
pause
