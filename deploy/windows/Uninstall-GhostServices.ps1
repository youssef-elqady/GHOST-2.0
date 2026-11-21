<# Removes the GHOST API service and the frontend task. It does NOT touch any database, backup or log file. #>
$ErrorActionPreference = 'Stop'
if (Get-ScheduledTask -TaskName 'GHOST Frontend' -ErrorAction SilentlyContinue) { Stop-ScheduledTask 'GHOST Frontend' -ErrorAction SilentlyContinue; Unregister-ScheduledTask 'GHOST Frontend' -Confirm:$false }
if (Get-Service 'GHOST API' -ErrorAction SilentlyContinue) { Stop-Service 'GHOST API' -Force -ErrorAction SilentlyContinue; sc.exe delete 'GHOST API' | Out-Null }
Write-Host 'Removed service and task. Databases, backups and logs were not touched.'
