<#
 Installs GHOST on THIS PC as: Windows Service "GHOST API" (auto start, delayed, restart on crash, depends on SQL Server)
 and a startup task "GHOST Frontend" (waits for the API to be READY, restarts on crash).
 Run from an elevated PowerShell in the repo root. Localhost only: nothing here opens a network port.

 WHAT THIS SCRIPT NEVER DOES: create, migrate, bootstrap, reset or delete any database. Apply migrations to the
 live database yourself, only after you approve it. Until then /health/ready reports migrationsApplied=false.

 Example:
   .\deploy\windows\Install-GhostServices.ps1 -SqlServiceName MSSQLSERVER -InstallDir C:\GHOST -LogDir C:\GHOST\logs `
       -BackupDir D:\GhostBackups -ExternalBackupDir E:\GhostBackups -ExternalDriveId <id from New-BackupDriveMarker.ps1>
 You will be asked for the connection string and the JWT signing key. They are stored in the service's own registry
 key (readable by administrators only) and are never written to files in the repo or printed.
#>
param(
  [string]$SqlServiceName = 'MSSQLSERVER',
  [string]$InstallDir = 'C:\GHOST',
  [string]$LogDir = 'C:\GHOST\logs',
  [string]$BackupDir,
  [string]$ExternalBackupDir,
  [string]$ExternalDriveMarker,
  [string]$ExternalDriveId,
  [string]$ApiUrl = 'http://localhost:5153',
  [string]$FrontendUrl = 'http://localhost:3000',
  [int]$FrontendPort = 3000
)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script as Administrator.' }
foreach ($u in @($ApiUrl, $FrontendUrl)) { $h = ([uri]$u).Host; if ($h -notin 'localhost','127.0.0.1','[::1]') { throw "$u is not a loopback address. GHOST is local-only." } }
if (-not (Get-Service -Name $SqlServiceName -ErrorAction SilentlyContinue)) { throw "SQL Server service '$SqlServiceName' was not found. Check the name with: Get-Service *SQL*" }
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$conn = Read-Host 'Connection string for the LIVE database (input is hidden)' -AsSecureString
$connPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($conn))
$keyPlain = [Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
Write-Host 'Generated a random JWT signing key (not shown).'

New-Item -ItemType Directory -Force $InstallDir, $LogDir | Out-Null
Write-Host '1/5 Publishing API...'
dotnet publish "$repo\backend\src\GHOST.API\GHOST.API.csproj" -c Release -o "$InstallDir\api" --nologo
if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }

Write-Host '2/5 Building frontend...'
Push-Location "$repo\frontend"
try {
  $env:NEXT_TELEMETRY_DISABLED = '1'
  $env:NEXT_PUBLIC_GHOST_API_BASE_URL = $ApiUrl
  pnpm install --frozen-lockfile
  if ($LASTEXITCODE -ne 0) { throw 'pnpm install failed.' }
  pnpm build
  if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }
if (Test-Path "$InstallDir\frontend") { Remove-Item "$InstallDir\frontend" -Recurse -Force }
Copy-Item "$repo\frontend" "$InstallDir\frontend" -Recurse -Exclude '.git'

Write-Host '3/5 Creating the GHOST API service...'
$svc = 'GHOST API'
if (Get-Service -Name $svc -ErrorAction SilentlyContinue) { Stop-Service $svc -Force; sc.exe delete $svc | Out-Null; Start-Sleep 2 }
New-Service -Name $svc -BinaryPathName "`"$InstallDir\api\GHOST.API.exe`"" -DisplayName 'GHOST API' -StartupType Automatic -Description 'GHOST venue management API (localhost only)' | Out-Null
sc.exe config $svc start= delayed-auto depend= $SqlServiceName | Out-Null
sc.exe failure $svc reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
sc.exe failureflag $svc 1 | Out-Null
$env = @(
  "ASPNETCORE_ENVIRONMENT=Production", "ASPNETCORE_URLS=$ApiUrl",
  "ConnectionStrings__DefaultConnection=$connPlain",
  "Authentication__Jwt__Issuer=ghost-local", "Authentication__Jwt__Audience=ghost-local",
  "Authentication__Jwt__SigningKey=$keyPlain", "Authentication__Jwt__AccessTokenLifetimeMinutes=10",
  "Authentication__RefreshToken__AllowedOrigins__0=$FrontendUrl",
  "Logging__File__Directory=$LogDir"
)
if ($BackupDir) { $env += "Backup__Enabled=true", "Backup__Directory=$BackupDir", "Backup__RetentionCount=7", "Backup__DailyAt=03:00", "Backup__BackupOnShiftClose=true" }
if ($ExternalBackupDir) { $env += "Backup__ExternalDirectory=$ExternalBackupDir" }
if ($ExternalDriveMarker) { $env += "Backup__ExternalDriveMarker=$ExternalDriveMarker" }
if ($ExternalDriveId) { $env += "Backup__ExternalDriveId=$ExternalDriveId" }
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$svc" -Name Environment -Value $env -Type MultiString
$connPlain = $null; $keyPlain = $null; $env = $null

Write-Host '4/5 Creating the frontend startup task...'
$task = 'GHOST Frontend'
$arg = "-NoProfile -ExecutionPolicy Bypass -File `"$repo\deploy\windows\Start-GhostFrontend.ps1`" -FrontendDir `"$InstallDir\frontend`" -ApiUrl $ApiUrl -Port $FrontendPort -LogDir `"$LogDir`""
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arg
$trigger = New-ScheduledTaskTrigger -AtStartup
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null

Write-Host '5/5 Starting...'
Start-Service $svc
Start-ScheduledTask -TaskName $task
Write-Host "Installed. Verify with: .\deploy\windows\Check-GhostHealth.ps1 -SqlServiceName $SqlServiceName"
Write-Host 'Reminder: use Chrome or Edge on this PC (Secure cookies on http://localhost).'
