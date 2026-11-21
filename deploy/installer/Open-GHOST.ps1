<# Desktop icon: waits (with a small window) until GHOST is ready, then opens it in Edge or Chrome. #>
Add-Type -AssemblyName System.Windows.Forms
$url = 'http://localhost:3000'
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
  try {
    $r = Invoke-RestMethod -Uri 'http://localhost:5153/health/ready' -TimeoutSec 3
    if ($r.status -eq 'Ready') { Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 3 | Out-Null; $ready = $true; break }
  } catch { }
  Start-Sleep 3
}
if (-not $ready) { [System.Windows.Forms.MessageBox]::Show('GHOST is not ready yet. Wait one minute and click the icon again. If it still fails, call your technician.','GHOST') | Out-Null; exit 1 }
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$chrome = "$env:ProgramFiles\Google\Chrome\Application\chrome.exe"
if (Test-Path $edge) { Start-Process $edge $url } elseif (Test-Path $chrome) { Start-Process $chrome $url } else { [System.Windows.Forms.MessageBox]::Show('Install Edge or Chrome to use GHOST.','GHOST') | Out-Null }
