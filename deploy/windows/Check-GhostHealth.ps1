<# After a full PC restart: checks every layer separately. A running service is NOT proof that GHOST is ready. #>
param([string]$SqlServiceName = 'MSSQLSERVER', [string]$ApiUrl = 'http://localhost:5153', [string]$FrontendUrl = 'http://localhost:3000')
$bad = 0
function Check($name, [scriptblock]$test) {
  try { $r = & $test; if ($r) { Write-Host ("PASS  {0}" -f $name) } else { Write-Host ("FAIL  {0}" -f $name); $script:bad++ } }
  catch { Write-Host ("FAIL  {0}  ({1})" -f $name, $_.Exception.Message); $script:bad++ }
}
Check 'SQL Server service running'          { (Get-Service $SqlServiceName).Status -eq 'Running' }
Check 'GHOST API service running'            { (Get-Service 'GHOST API').Status -eq 'Running' }
Check 'API live'                             { (Invoke-RestMethod "$ApiUrl/health/live" -TimeoutSec 5).status -eq 'Live' }
Check 'API ready (database + all migrations)'{ (Invoke-RestMethod "$ApiUrl/health/ready" -TimeoutSec 10).status -eq 'Ready' }
Check 'Frontend answers'                     { (Invoke-WebRequest $FrontendUrl -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200 }
Check 'API is NOT reachable from the LAN IP' {
  $ip = (Get-NetIPAddress -AddressFamily IPv4 | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | Select-Object -First 1).IPAddress
  if (-not $ip) { return $true }
  $port = ([uri]$ApiUrl).Port
  -not (Test-NetConnection -ComputerName $ip -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue)
}
Check 'Latest log file written in last 5 minutes' { (Get-ChildItem 'C:\GHOST\logs\ghost-api-*.log' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime -gt (Get-Date).AddMinutes(-5) }
if ($bad -gt 0) { Write-Host "$bad check(s) failed. Do NOT go live."; exit 1 } else { Write-Host 'All checks passed.' }
