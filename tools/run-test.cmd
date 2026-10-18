@echo off
rem Тестовый запуск оболочки без службы: вход demo / demo, выход Ctrl+Shift+F12 и PIN 0000.
set CLUBSHELL_TEST=1
start "" "%~dp0ClubShell.Locker.exe"
