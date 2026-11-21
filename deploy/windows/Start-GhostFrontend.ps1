<# Waits until the API reports READY (database reachable and all migrations applied), then runs the frontend on loopback. Started by the "GHOST Frontend" task at boot. #>
param([Parameter(Mandatory)][string]$FrontendDir, [string]$ApiUrl = 'http://localhost:5153', [int]$Port = 3000, [string]$LogDir = 'C:\GHOST\logs')
$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force $LogDir | Out-Null
$log = Join-Path $LogDir ("frontend-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
function Log($m) { Add-Content -Path $log -Value ("{0} {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) }
$deadline = (Get-Date).AddMinutes(15)
while ((Get-Date) -lt $deadline) {
  try { $r = Invoke-RestMethod -Uri "$ApiUrl/health/ready" -TimeoutSec 5; if ($r.status -eq 'Ready') { Log 'API ready'; break } }
  catch { }
  Log 'Waiting for API to be ready...'
  Start-Sleep -Seconds 10
}
if ((Get-Date) -ge $deadline) { Log 'API never became ready. Frontend not started.'; exit 1 }
$env:NEXT_TELEMETRY_DISABLED = '1'
Set-Location $FrontendDir
Log "Starting frontend on 127.0.0.1:$Port"
& pnpm exec next start -H 127.0.0.1 -p $Port *>> $log
exit $LASTEXITCODE
