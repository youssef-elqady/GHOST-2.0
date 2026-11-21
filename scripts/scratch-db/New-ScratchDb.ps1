<#
 Creates a NEW empty scratch database and applies all migrations to it.
 SAFETY: refuses any name that is not GHOST_SCRATCH_*, refuses to touch a database that already exists
 (it never drops, resets or recreates anything), and never reads GHOST2 settings.
 Usage:  .\scripts\scratch-db\New-ScratchDb.ps1 -Server "localhost" -Name GHOST_SCRATCH_01
         (Windows authentication. For SQL login add -User x -Password (Read-Host -AsSecureString))
#>
param(
  [Parameter(Mandatory)][string]$Server,
  [Parameter(Mandatory)][ValidatePattern('^GHOST_SCRATCH_[A-Za-z0-9_]{1,40}$')][string]$Name,
  [string]$User,
  [securestring]$Password
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

function New-Conn([string]$db) {
  $b = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
  $b['Data Source'] = $Server; $b['Initial Catalog'] = $db; $b['TrustServerCertificate'] = $true; $b['Encrypt'] = $true
  if ($User) {
    $b['User ID'] = $User
    $b['Password'] = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password))
  } else { $b['Integrated Security'] = $true }
  return $b.ConnectionString
}

$master = New-Object System.Data.SqlClient.SqlConnection (New-Conn 'master')
$master.Open()
$cmd = $master.CreateCommand()
$cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @n"
[void]$cmd.Parameters.AddWithValue('@n', $Name)
if ([int]$cmd.ExecuteScalar() -gt 0) { throw "Database $Name already exists. Choose a new name. This script never touches an existing database." }
$cmd.Parameters.Clear()
$cmd.CommandText = "CREATE DATABASE [$Name]"
[void]$cmd.ExecuteNonQuery()
$master.Close()
Write-Host "Created new empty database $Name"

$env:Authentication__Jwt__Issuer = 'scratch'; $env:Authentication__Jwt__Audience = 'scratch'
$env:Authentication__Jwt__SigningKey = '0123456789012345678901234567890123456789'
$env:Authentication__Jwt__AccessTokenLifetimeMinutes = '10'
$env:ConnectionStrings__DefaultConnection = (New-Conn $Name)
Push-Location $repo
try {
  dotnet ef database update -p backend/src/GHOST.Infrastructure -s backend/src/GHOST.API --connection (New-Conn $Name)
  if ($LASTEXITCODE -ne 0) { throw "Migrations failed on $Name. Keep the output: this is a test result." }
} finally { Pop-Location }
Write-Host "All migrations applied to $Name. Next: .\scripts\scratch-db\Run-ScratchSuite.ps1 -Server $Server -Name $Name"
