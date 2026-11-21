@echo off
rem Plug in the external backup drive first. Type its letter (for example E) when asked.
set /p L=Drive letter of the external backup drive: 
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0New-BackupDriveMarker.ps1" -DriveRoot %L%:\
echo.
echo Write down the ExternalDriveId above. The installer asks for it (-ExternalDriveId).
pause
