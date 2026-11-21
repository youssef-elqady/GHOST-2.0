<# Run ONCE with the external backup drive plugged in. Writes a marker file at the root of that drive and prints the id to put in Backup:ExternalDriveId. Backups are then only copied to a drive that carries this exact marker, whatever its drive letter. #>
param([Parameter(Mandatory)][ValidatePattern('^[A-Za-z]:\\?$')][string]$DriveRoot)
$root = $DriveRoot.TrimEnd('\') + '\'
if (-not (Test-Path $root)) { throw "$root is not connected." }
$marker = Join-Path $root '.ghost-backup-drive'
$letter = $root.Substring(0,1)
$serial = ('{0:X8}' -f [uint32](Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='${letter}:'").VolumeSerialNumber -as [uint32])
$serial = (Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='${letter}:'").VolumeSerialNumber
if (-not $serial) { throw 'Could not read the volume serial of this drive.' }
if (Test-Path $marker) { $old = (Get-Content $marker -Raw).Trim(); Write-Host "Marker already exists."; Write-Host "Marker file: $marker"; Write-Host "ExternalDriveId: $old@$serial"; return }
$id = [guid]::NewGuid().ToString('N')
Set-Content -Path $marker -Value $id -NoNewline
Write-Host "Marker file: $marker"
Write-Host "ExternalDriveId: $id@$serial"  # marker id + volume serial; a letter change is fine, a copied marker on another drive is refused
Write-Host 'Use -ExternalDriveMarker with the marker file path and -ExternalDriveId with this id in Install-GhostServices.ps1.'
