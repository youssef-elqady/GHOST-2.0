<# Health report for the technician. Read only. #>
$ok = $true
function Line($name, $good, $detail) { $c = if ($good) { 'Green' } else { 'Red' }; Write-Host ("{0,-28} {1}  {2}" -f $name, $(if ($good) { 'OK' } else { 'PROBLEM' }), $detail) -ForegroundColor $c; if (-not $good) { $script:ok = $false } }
$api = Get-Service 'GHOST API' -ErrorAction SilentlyContinue
Line 'GHOST API service' ($api -and $api.Status -eq 'Running') ($api.Status)
$t = Get-ScheduledTask -TaskName 'GHOST Frontend' -ErrorAction SilentlyContinue
Line 'Screen startup task' ($null -ne $t) ($t.State)
try { $l = Invoke-RestMethod 'http://localhost:5153/health/live' -TimeoutSec 5; Line 'API alive' $true '' } catch { Line 'API alive' $false $_.Exception.Message }
try { $r = Invoke-RestMethod 'http://localhost:5153/health/ready' -TimeoutSec 5; Line 'Database and schema ready' ($r.status -eq 'Ready') ($r | ConvertTo-Json -Compress) } catch { Line 'Database and schema ready' $false 'not ready (database down or schema not current)' }
try { Invoke-WebRequest 'http://localhost:3000' -UseBasicParsing -TimeoutSec 5 | Out-Null; Line 'Screen answers' $true '' } catch { Line 'Screen answers' $false $_.Exception.Message }
$listen = Get-NetTCPConnection -State Listen -LocalPort 5153,3000 -ErrorAction SilentlyContinue
$wide = $listen | Where-Object { $_.LocalAddress -notin '127.0.0.1','::1' }
Line 'Listening on this PC only' (-not $wide) (($listen | ForEach-Object { "$($_.LocalAddress):$($_.LocalPort)" }) -join ', ')
Write-Host ''; Write-Host $(if ($ok) { 'All checks passed.' } else { 'Some checks failed. See C:\GHOST\logs.' })
