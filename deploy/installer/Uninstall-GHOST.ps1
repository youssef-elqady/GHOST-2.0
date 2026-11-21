<# Removes the GHOST services, startup task and shortcuts. NEVER deletes the database. Backups are deleted only with
   -RemoveBackups AND the typed phrase. Program files are removed only with -RemoveProgramFiles. #>
param([string]$InstallDir = 'C:\GHOST', [switch]$RemoveProgramFiles, [switch]$RemoveBackups, [string]$BackupDir = 'C:\GHOST\backups')
$ErrorActionPreference = 'Continue'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Write-Host 'Run as administrator.'; exit 1 }
Stop-ScheduledTask -TaskName 'GHOST Frontend' -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'GHOST Frontend' -Confirm:$false -ErrorAction SilentlyContinue
if (Get-Service 'GHOST API' -ErrorAction SilentlyContinue) { Stop-Service 'GHOST API' -Force; & sc.exe delete 'GHOST API' | Out-Null }
Get-Process -Name node -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir\app\*" } | Stop-Process -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'GHOST.lnk') -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonStartup')) 'GHOST.lnk') -ErrorAction SilentlyContinue
Write-Host 'Services, task and shortcuts removed.'
if ($RemoveProgramFiles) { Remove-Item (Join-Path $InstallDir 'app') -Recurse -Force -ErrorAction SilentlyContinue; Write-Host 'Program files removed. Logs are kept.' }
if ($RemoveBackups) {
  $a = Read-Host "Type DELETE BACKUPS to permanently delete everything in $BackupDir"
  if ($a -ceq 'DELETE BACKUPS') { Remove-Item $BackupDir -Recurse -Force; Write-Host 'Backups deleted.' } else { Write-Host 'Backups kept.' }
}
Write-Host 'The GHOST database was NOT touched. It stays in SQL Server. Backups were kept unless you confirmed above.'
