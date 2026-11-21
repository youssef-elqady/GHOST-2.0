<# Run ONCE with the external backup drive plugged in. Writes a marker file at the root of that drive and prints the id to put in Backup:ExternalDriveId. Backups are then only copied to a drive that carries this exact marker, whatever its drive letter. #>
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:\\?$')][string]$DriveRoot)
$root = $DriveRoot.TrimEnd('\') + '\'
if (-not (Test-Path $root)) { throw "$root is not connected." }
$marker = Join-Path $root '.ghost-backup-drive'
if (Test-Path $marker) { Write-Host "Marker already exists. Id: $((Get-Content $marker -Raw).Trim())"; Write-Host "Marker file: $marker"; return }
$id = [guid]::NewGuid().ToString('N')
Set-Content -Path $marker -Value $id -NoNewline
Write-Host "Marker file: $marker"
Write-Host "ExternalDriveId: $id"
Write-Host 'Use -ExternalDriveMarker with the marker file path and -ExternalDriveId with this id in Install-GhostServices.ps1.'
