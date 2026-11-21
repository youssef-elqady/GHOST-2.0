@echo off
rem Right click and choose "Run as administrator". Removes the services and icons. Your database and backups are kept.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-GHOST.ps1" %*
echo.
pause
