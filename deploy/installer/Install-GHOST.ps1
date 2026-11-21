<#
 GHOST production installer for ONE Windows PC (localhost only). Run via Install-GHOST.cmd (right click, Run as administrator).
 Safe by design:
   * never touches an existing database: if the database name already exists it stops (see -UseExistingDatabase)
   * creates a NEW empty database only after you type the confirmation phrase
   * never opens a network port: API and screen listen on this PC only
 Re-running is safe: files and services are refreshed, secrets and data are kept.
#>
[CmdletBinding()]
param(
  [string]$InstallDir = 'C:\GHOST',
  [string]$DataBackupDir = 'C:\GHOST\backups',
  [string]$ExternalBackupDir,
  [string]$ExternalDriveId,
  [string]$SqlInstance,                 # e.g. GHOSTSQL or MSSQLSERVER. Default: detect, or install SQL Express from prereq\
  [string]$DatabaseName = 'GHOST',
  [switch]$UseExistingDatabase,         # the database already exists and was prepared by the owner: no create, no migrate
  [switch]$NoAutoOpen
)
$ErrorActionPreference = 'Stop'
$pkg = Split-Path -Parent $PSScriptRoot
$logDir = Join-Path $InstallDir 'logs'
New-Item -ItemType Directory -Force $InstallDir, $logDir | Out-Null
Start-Transcript -Path (Join-Path $logDir ("install-{0}.log" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))) | Out-Null
function Step($m) { Write-Host ""; Write-Host "== $m" -ForegroundColor Cyan }
function Fail($m) { Write-Host "STOPPED: $m" -ForegroundColor Red; try { Stop-Transcript | Out-Null } catch {}; exit 1 }
try {
  Step '1/9 Checking this PC'
  if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { Fail 'Run as administrator (right click the installer, Run as administrator).' }
  if (-not [Environment]::Is64BitOperatingSystem) { Fail 'GHOST needs 64-bit Windows 10 or newer.' }
  if ([Environment]::OSVersion.Version.Major -lt 10) { Fail 'GHOST needs Windows 10 or newer.' }
  $drive = (Get-Item $InstallDir).PSDrive
  if ($drive.Free -lt 5GB) { Fail ("Less than 5 GB free on drive {0}:" -f $drive.Name) }
  foreach ($f in 'api\GHOST.API.exe','frontend\node.exe','frontend\server.js','migrations\ghost-migrate.exe','bootstrap\GHOST.Bootstrap.exe') {
    if (-not (Test-Path (Join-Path $pkg $f))) { Fail "The package is incomplete: $f is missing. Copy the whole GHOST folder again." }
  }
  # Verify the package was not damaged in copying.
  $sums = Join-Path $pkg 'SHA256SUMS.txt'
  if (Test-Path $sums) {
    $bad = 0
    foreach ($line in Get-Content $sums) {
      if ($line -match '^([0-9a-f]{64})\s+\*?(.+)$') {
        $file = Join-Path $pkg ($Matches[2] -replace '^\./','')
        if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash.ToLower() -ne $Matches[1]) { $bad++ }
      }
    }
    if ($bad -gt 0) { Fail "$bad package file(s) are damaged or missing. Copy the GHOST folder again from the original drive." }
    Write-Host 'Package files verified.'
  } else { Write-Host 'WARNING: SHA256SUMS.txt not found, package integrity not verified.' -ForegroundColor Yellow }
  foreach ($port in 5153, 3000) {
    $c = Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue
    if ($c) { $owner = (Get-Process -Id $c[0].OwningProcess -ErrorAction SilentlyContinue).ProcessName; if ($owner -notin 'GHOST.API','node') { Fail "Port $port is used by '$owner'. Close it first." } }
  }

  Step '2/9 SQL Server'
  $instances = @(Get-Service -Name 'MSSQLSERVER','MSSQL$*' -ErrorAction SilentlyContinue)
  if (-not $SqlInstance) {
    if ($instances.Count -eq 1) { $SqlInstance = if ($instances[0].Name -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { $instances[0].Name -replace '^MSSQL\$','' } }
    elseif ($instances.Count -gt 1) { Write-Host ($instances | ForEach-Object Name | Out-String); Fail 'More than one SQL Server found. Re-run with -SqlInstance <name>.' }
  }
  if (-not $SqlInstance) {
    $setup = Get-ChildItem (Join-Path $pkg 'prereq') -Filter 'SQLEXPR*.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $setup) { Fail 'No SQL Server found and no SQLEXPR*.exe in the prereq folder. Put the SQL Server Express installer (downloaded from Microsoft) in the prereq folder and run again.' }
    Write-Host "Installing SQL Server Express from $($setup.Name). This takes 10-20 minutes..."
    $SqlInstance = 'GHOSTSQL'
    # Local only: no TCP, no named pipes (shared memory). Administrators and the GHOST service account get access.
    $args = @('/Q','/ACTION=Install','/IACCEPTSQLSERVERLICENSETERMS','/FEATURES=SQLEngine',"/INSTANCENAME=$SqlInstance",
      '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators','NT AUTHORITY\SYSTEM','/TCPENABLED=0','/NPENABLED=0','/UPDATEENABLED=0','/SECURITYMODE=Windows')
    $p = Start-Process -FilePath $setup.FullName -ArgumentList $args -Wait -PassThru
    if ($p.ExitCode -notin 0, 3010) { Fail "SQL Server setup failed (code $($p.ExitCode)). See the Summary log under C:\Program Files\Microsoft SQL Server\*\Setup Bootstrap\Log." }
    $instances = @(Get-Service -Name "MSSQL`$$SqlInstance" -ErrorAction SilentlyContinue)
  }
  $sqlSvcName = if ($SqlInstance -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { "MSSQL`$$SqlInstance" }
  $sqlSvc = Get-Service -Name $sqlSvcName -ErrorAction SilentlyContinue
  if (-not $sqlSvc) { Fail "SQL Server service $sqlSvcName was not found." }
  if ($sqlSvc.Status -ne 'Running') { Start-Service $sqlSvcName; $sqlSvc.WaitForStatus('Running','00:02:00') }
  $server = if ($SqlInstance -eq 'MSSQLSERVER') { '(local)' } else { "(local)\$SqlInstance" }
  Write-Host "Using SQL Server: $server"

  function Invoke-Sql([string]$db, [string]$sql, [switch]$Scalar) {
    $cn = New-Object System.Data.SqlClient.SqlConnection "Server=$server;Database=$db;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=15"
    $cn.Open(); try { $cmd = $cn.CreateCommand(); $cmd.CommandText = $sql; $cmd.CommandTimeout = 300; if ($Scalar) { $cmd.ExecuteScalar() } else { [void]$cmd.ExecuteNonQuery() } } finally { $cn.Close() }
  }
  try { $edition = Invoke-Sql 'master' "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(200))" -Scalar } catch { Fail "Cannot connect to $server as the current Windows user: $($_.Exception.Message)" }
  Write-Host "SQL Server edition: $edition"

  Step '3/9 Database'
  if ($DatabaseName -notmatch '^[A-Za-z0-9_]{1,40}$') { Fail 'Database name may contain only letters, digits and underscore.' }
  $exists = [int](Invoke-Sql 'master' "SELECT COUNT(*) FROM sys.databases WHERE name = N'$DatabaseName'" -Scalar) -gt 0
  $connStr = "Server=$server;Database=$DatabaseName;Integrated Security=True;TrustServerCertificate=True"
  if ($exists -and -not $UseExistingDatabase) { Fail "Database '$DatabaseName' already exists. The installer will NOT touch an existing database. If it is the right one and already prepared, run again with -UseExistingDatabase. Otherwise choose another -DatabaseName." }
  if (-not $exists) {
    if ($UseExistingDatabase) { Fail "-UseExistingDatabase was given but '$DatabaseName' does not exist." }
    Write-Host "A NEW empty database named '$DatabaseName' will be created on $server."
    $answer = Read-Host "Type CREATE $DatabaseName to confirm"
    if ($answer -cne "CREATE $DatabaseName") { Fail 'Not confirmed. Nothing was changed.' }
    Invoke-Sql 'master' "CREATE DATABASE [$DatabaseName]"
    Write-Host 'Database created. Applying the GHOST schema...'
    & (Join-Path $pkg 'migrations\ghost-migrate.exe') --connection $connStr
    if ($LASTEXITCODE -ne 0) { Fail 'Schema setup failed. The new empty database was left in place for inspection (it holds no business data).' }
  } else { Write-Host "Using the existing database '$DatabaseName' as is. No schema change is made. If /health/ready says migrations are pending, apply them yourself after your approval." }

  Step '4/9 Copying program files'
  $app = Join-Path $InstallDir 'app'
  foreach ($s in 'GHOST API','GHOST Frontend') { if (Get-Service $s -ErrorAction SilentlyContinue) { Stop-Service $s -Force -ErrorAction SilentlyContinue } }
  Stop-ScheduledTask -TaskName 'GHOST Frontend' -ErrorAction SilentlyContinue
  Get-Process -Name node -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$app\*" } | Stop-Process -Force -ErrorAction SilentlyContinue
  Start-Sleep 2
  New-Item -ItemType Directory -Force $app | Out-Null
  foreach ($d in 'api','frontend','bootstrap','migrations','installer') {
    $dst = Join-Path $app $d
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    Copy-Item (Join-Path $pkg $d) $dst -Recurse
  }

  Step '5/9 Backup folders'
  New-Item -ItemType Directory -Force $DataBackupDir | Out-Null
  $sqlAccount = (Get-CimInstance Win32_Service -Filter "Name='$sqlSvcName'").StartName
  & icacls $DataBackupDir /grant "${sqlAccount}:(OI)(CI)M" /T | Out-Null
  Write-Host "Local backups: $DataBackupDir (SQL Server account '$sqlAccount' can write there)"

  Step '6/9 GHOST API service'
  $svc = 'GHOST API'
  $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$svc"
  $jwtKey = $null
  if (Test-Path $regPath) {   # keep the same signing key on re-install so users stay logged in
    $old = (Get-ItemProperty $regPath -ErrorAction SilentlyContinue).Environment
    $line = $old | Where-Object { $_ -like 'Authentication__Jwt__SigningKey=*' } | Select-Object -First 1
    if ($line) { $jwtKey = $line.Substring($line.IndexOf('=') + 1) }
    Stop-Service $svc -Force -ErrorAction SilentlyContinue; & sc.exe delete $svc | Out-Null; Start-Sleep 3
  }
  if (-not $jwtKey) { $bytes = New-Object byte[] 48; [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes); $jwtKey = [Convert]::ToBase64String($bytes) }
  New-Service -Name $svc -BinaryPathName "`"$app\api\GHOST.API.exe`"" -DisplayName 'GHOST API' -StartupType Automatic -Description 'GHOST venue management API (this PC only)' | Out-Null
  & sc.exe config $svc start= delayed-auto depend= $sqlSvcName | Out-Null
  & sc.exe failure $svc reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
  & sc.exe failureflag $svc 1 | Out-Null
  $envList = @('ASPNETCORE_ENVIRONMENT=Production','ASPNETCORE_URLS=http://localhost:5153',
    "ConnectionStrings__DefaultConnection=$connStr",
    'Authentication__Jwt__Issuer=ghost-local','Authentication__Jwt__Audience=ghost-local',"Authentication__Jwt__SigningKey=$jwtKey",'Authentication__Jwt__AccessTokenLifetimeMinutes=10',
    'Authentication__RefreshToken__AllowedOrigins__0=http://localhost:3000',
    "Logging__File__Directory=$logDir",
    'Backup__Enabled=true',"Backup__Directory=$DataBackupDir",'Backup__RetentionCount=7','Backup__DailyAt=03:00','Backup__BackupOnShiftClose=true')
  if ($ExternalBackupDir) { $envList += "Backup__ExternalDirectory=$ExternalBackupDir", "Backup__ExternalDriveMarker=$((Split-Path -Qualifier $ExternalBackupDir) + '\.ghost-backup-drive')" }
  if ($ExternalDriveId) { $envList += "Backup__ExternalDriveId=$ExternalDriveId" }
  Set-ItemProperty -Path $regPath -Name Environment -Value $envList -Type MultiString
  $jwtKey = $null; $envList = $null

  Step '7/9 Screen (frontend) startup task'
  $task = 'GHOST Frontend'
  $arg = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\installer\Start-GHOST-Frontend.ps1`" -AppDir `"$app`" -LogDir `"$logDir`""
  $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arg
  $trigger = New-ScheduledTaskTrigger -AtStartup
  $settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
  Register-ScheduledTask -TaskName $task -Action $action -Trigger $trigger -Settings $settings -User 'SYSTEM' -RunLevel Highest -Force | Out-Null

  Step '8/9 Desktop shortcut'
  $browser = @("$env:ProgramFiles (x86)\Microsoft\Edge\Application\msedge.exe","$env:ProgramFiles\Google\Chrome\Application\chrome.exe","${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $browser) { Write-Host 'WARNING: Edge or Chrome was not found. GHOST needs Chrome or Edge.' -ForegroundColor Yellow }
  $wsh = New-Object -ComObject WScript.Shell
  $lnk = $wsh.CreateShortcut((Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'GHOST.lnk'))
  $lnk.TargetPath = 'powershell.exe'
  $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$app\installer\Open-GHOST.ps1`""
  $lnk.WorkingDirectory = $app; $lnk.Description = 'Open GHOST'; $lnk.Save()
  if (-not $NoAutoOpen) {
    $startup = Join-Path ([Environment]::GetFolderPath('CommonStartup')) 'GHOST.lnk'
    $l2 = $wsh.CreateShortcut($startup); $l2.TargetPath = 'powershell.exe'; $l2.Arguments = $lnk.Arguments; $l2.Save()
  }

  Step '9/9 Starting and checking'
  Start-Service $svc
  Start-ScheduledTask -TaskName $task
  $ready = $false
  for ($i = 0; $i -lt 40; $i++) {
    try { $r = Invoke-RestMethod -Uri 'http://localhost:5153/health/ready' -TimeoutSec 5; if ($r.status -eq 'Ready') { $ready = $true; break } } catch { }
    Start-Sleep 3
  }
  if ($ready) { Write-Host 'API is READY (database reachable, schema up to date).' -ForegroundColor Green }
  else { Write-Host 'API is NOT ready. Run Check-GHOST.ps1 and read the log in C:\GHOST\logs.' -ForegroundColor Yellow }

  if ($ready -and -not $UseExistingDatabase) {
    $hasAdmin = $false
    Write-Host ''
    $a = Read-Host 'Create the first Admin account now? (y/N)'
    if ($a -match '^(y|yes)$') {
      $env:GHOST_CONNECTION_STRING = $connStr
      & (Join-Path $app 'bootstrap\GHOST.Bootstrap.exe')
      Remove-Item Env:\GHOST_CONNECTION_STRING -ErrorAction SilentlyContinue
    }
  }
  Write-Host ''
  Write-Host 'Done. Use the GHOST icon on the Desktop (Chrome or Edge). Logs: C:\GHOST\logs. Backups: ' $DataBackupDir -ForegroundColor Green
} catch { Write-Host ("STOPPED: " + $_.Exception.Message) -ForegroundColor Red; try { Stop-Transcript | Out-Null } catch {}; exit 1 }
try { Stop-Transcript | Out-Null } catch {}
