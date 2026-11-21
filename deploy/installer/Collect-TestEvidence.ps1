<# Run after each clean-machine test step. Collects non-secret evidence into one folder you can send back: OS, services, health, recent logs, installed files hashes. Never reads connection strings, passwords or the database content. #>
param([string]$InstallDir = 'C:\GHOST', [string]$Out = "$env:USERPROFILE\Desktop\GHOST-evidence-$(Get-Date -Format yyyyMMdd-HHmmss)")
New-Item -ItemType Directory -Path $Out -Force | Out-Null
$os = Get-CimInstance Win32_OperatingSystem
"$($os.Caption) $($os.Version) build $($os.BuildNumber); computer $env:COMPUTERNAME; time $(Get-Date -Format o)" | Set-Content "$Out\system.txt"
"Dev tools present (should all be False on a clean test PC):" | Add-Content "$Out\system.txt"
foreach ($t in 'node','dotnet','git','devenv') { "$t : $([bool](Get-Command $t -ErrorAction SilentlyContinue))" | Add-Content "$Out\system.txt" }
Get-Service | Where-Object { $_.Name -like 'GHOST*' -or $_.Name -like 'MSSQL*GHOSTSQL*' } | Select-Object Name,Status,StartType | Format-Table -AutoSize | Out-String | Set-Content "$Out\services.txt"
'http://localhost:5153/health/live','http://localhost:5153/health/ready','http://localhost:3000/' | ForEach-Object {
  $u = $_
  try { $r = Invoke-WebRequest -UseBasicParsing -Uri $u -TimeoutSec 8; "$u -> $($r.StatusCode)" } catch { "$u -> FAILED $($_.Exception.Message)" }
} | Set-Content "$Out\health.txt"
Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -in 5153,3000,1433 } | Select-Object LocalAddress,LocalPort | Format-Table | Out-String | Set-Content "$Out\listening.txt"
if (Test-Path "$InstallDir\logs") { Get-ChildItem "$InstallDir\logs" | Sort-Object LastWriteTime -Descending | Select-Object -First 5 | ForEach-Object { Copy-Item $_.FullName $Out } }
Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddHours(-6)} -ErrorAction SilentlyContinue | Where-Object { $_.ProviderName -match 'GHOST|MSSQL' } | Select-Object -First 50 TimeCreated,ProviderName,LevelDisplayName,Message | Format-List | Out-String | Set-Content "$Out\eventlog.txt"
Get-ChildItem "$InstallDir\backups","$InstallDir\Backups" -ErrorAction SilentlyContinue | Select-Object FullName,Length,LastWriteTime | Format-Table | Out-String | Set-Content "$Out\backups-list.txt"
Write-Host "Evidence folder: $Out (no secrets collected). Zip it and send it back."
