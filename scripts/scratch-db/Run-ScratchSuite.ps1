<#
 Runs the SQL Server scratch suite (tests SC-01..SC-09 and more) against an EXISTING scratch database.
 SAFETY: the test code itself refuses any database that is not GHOST_SCRATCH_* or GHOST_IT_*.
 The suite only inserts new rows. Output goes to scratch-results.trx next to this script: keep it as evidence.
#>
param(
  [Parameter(Mandatory)][string]$Server,
  [Parameter(Mandatory)][ValidatePattern('^GHOST_(SCRATCH|IT)_[A-Za-z0-9_]{1,40}$')][string]$Name,
  [string]$User,
  [securestring]$Password
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$b['Data Source'] = $Server; $b['Initial Catalog'] = $Name; $b['TrustServerCertificate'] = $true; $b['Encrypt'] = $true
if ($User) { $b['User ID'] = $User; $b['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)) } else { $b['Integrated Security'] = $true }
$env:GHOST_SCRATCH_TEST_CONNECTION_STRING = $b.ConnectionString
try {
  Push-Location $repo
  dotnet test backend/tests/GHOST.SqlServer.Tests/GHOST.SqlServer.Tests.csproj -c Release --logger "trx;LogFileName=$PSScriptRoot\scratch-results.trx" --logger "console;verbosity=normal"
} finally { Pop-Location; Remove-Item Env:GHOST_SCRATCH_TEST_CONNECTION_STRING -ErrorAction SilentlyContinue }
