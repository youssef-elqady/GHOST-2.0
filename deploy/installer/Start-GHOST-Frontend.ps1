<# Waits until the API says READY (database up, schema current), then runs the screen on this PC only. Started at boot by the task "GHOST Frontend". #>
param([Parameter(Mandatory)][string]$AppDir, [string]$LogDir = 'C:\GHOST\logs', [string]$ApiUrl = 'http://localhost:5153', [int]$Port = 3000)
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$log = Join-Path $LogDir ("frontend-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
function Log($m) { Add-Content -Path $log -Value ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) }
$deadline = (Get-Date).AddMinutes(20)
while ((Get-Date) -lt $deadline) {
  try { $r = Invoke-RestMethod -Uri "$ApiUrl/health/ready" -TimeoutSec 5; if ($r.status -eq 'Ready') { Log 'API ready'; break } } catch { }
  Start-Sleep -Seconds 5
}
if ((Get-Date) -ge $deadline) { Log 'API never became ready. Screen not started.'; exit 1 }
$env:PORT = "$Port"; $env:HOSTNAME = '127.0.0.1'; $env:NODE_ENV = 'production'; $env:NEXT_TELEMETRY_DISABLED = '1'
Set-Location (Join-Path $AppDir 'frontend')
Log "Starting screen on 127.0.0.1:$Port"
& .\node.exe server.js *>> $log
exit $LASTEXITCODE
