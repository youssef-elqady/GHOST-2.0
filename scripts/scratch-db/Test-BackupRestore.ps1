<#
 Backup and restore proof on SCRATCH databases only (test BK-01..BK-04):
  1. BACKUP DATABASE <scratch> WITH CHECKSUM  2. RESTORE VERIFYONLY WITH CHECKSUM
  3. RESTORE into a NEW database GHOST_SCRATCH_RESTORED_<stamp> (WITH MOVE, never REPLACE)
  4. Compare row counts of key tables between source and restored copy.
 It refuses source names that are not GHOST_SCRATCH_*, never uses REPLACE, never drops anything.
 Also prints SQL Server edition (decides SQL Server Agent and log-backup advice).
 Run it where SQL Server can write to -BackupDir (usually the SQL Server machine itself).
#>
param(
  [Parameter(Mandatory)][string]$Server,
  [Parameter(Mandatory)][ValidatePattern('^GHOST_SCRATCH_[A-Za-z0-9_]{1,40}$')][string]$Source,
  [Parameter(Mandatory)][string]$BackupDir,
  [string]$User,
  [securestring]$Password
)
$ErrorActionPreference = 'Stop'
$b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$b['Data Source'] = $Server; $b['Initial Catalog'] = 'master'; $b['TrustServerCertificate'] = $true; $b['Encrypt'] = $true
if ($User) { $b['User ID'] = $User; $b['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)) } else { $b['Integrated Security'] = $true }
$conn = New-Object System.Data.SqlClient.SqlConnection $b.ConnectionString
$conn.Open()
function Q([string]$sql) { $c = $conn.CreateCommand(); $c.CommandTimeout = 600; $c.CommandText = $sql; return $c }

$c = Q "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(200)), CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)), CAST(SERVERPROPERTY('EngineEdition') AS int), (SELECT recovery_model_desc FROM sys.databases WHERE name = '$Source')"
$r = $c.ExecuteReader(); [void]$r.Read()
Write-Host "EDITION: $($r[0]) | VERSION: $($r[1]) | EngineEdition: $($r[2]) | RecoveryModel($Source): $($r[3])"
$r.Close()

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$file = Join-Path $BackupDir "GHOST_SCRATCH_$stamp.bak"
[void](Q "BACKUP DATABASE [$Source] TO DISK = N'$file' WITH CHECKSUM, INIT").ExecuteNonQuery()
Write-Host "BK-01 backup written with CHECKSUM: $file"
[void](Q "RESTORE VERIFYONLY FROM DISK = N'$file' WITH CHECKSUM").ExecuteNonQuery()
Write-Host "BK-02 RESTORE VERIFYONLY passed"

$target = "GHOST_SCRATCH_RESTORED_$stamp"
$fl = Q "RESTORE FILELISTONLY FROM DISK = N'$file'"
$rd = $fl.ExecuteReader(); $moves = @(); $i = 0
$dataDir = $null
while ($rd.Read()) {
  $logical = $rd['LogicalName']; $type = $rd['Type']
  $ext = if ($type -eq 'L') { 'ldf' } else { 'mdf' }
  $moves += "MOVE N'$logical' TO N'$(Join-Path $BackupDir "$target`_$i.$ext")'"; $i++
}
$rd.Close()
[void](Q "RESTORE DATABASE [$target] FROM DISK = N'$file' WITH $($moves -join ', '), CHECKSUM, RECOVERY").ExecuteNonQuery()
Write-Host "BK-03 restored into NEW database $target (no REPLACE)"

$tables = 'Sales','Products','StockMovements','CashTransactions','Sessions','AuditLogs','Shifts','BackupRecords'
$bad = 0
foreach ($t in $tables) {
  $s = [int](Q "SELECT COUNT(*) FROM [$Source].dbo.[$t]").ExecuteScalar()
  $d = [int](Q "SELECT COUNT(*) FROM [$target].dbo.[$t]").ExecuteScalar()
  $ok = if ($s -eq $d) { 'OK' } else { $bad++; 'MISMATCH' }
  Write-Host ("BK-04 {0,-18} source={1,6} restored={2,6} {3}" -f $t, $s, $d, $ok)
}
$conn.Close()
if ($bad -gt 0) { throw "Restore verification found $bad mismatching table(s)." }
Write-Host "Backup/restore proof PASSED on scratch. Restored copy left in place for inspection: $target"
