@echo off
rem Right click this file and choose "Run as administrator".
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-GHOST.ps1" %*
echo.
pause
