$ErrorActionPreference = "Stop"

$config  = Get-Content "$PSScriptRoot\server-config.json" -Raw | ConvertFrom-Json
$appPath = $config.appPath
$dbPath  = $config.dbPath
$port    = if ($config.port) { $config.port } else { 5051 }

if (-not [System.IO.Path]::IsPathRooted($appPath)) {
    $appPath = Join-Path $PSScriptRoot $appPath
}
if (-not [System.IO.Path]::IsPathRooted($dbPath)) {
    $dbPath = Join-Path (Get-Location).Path $dbPath
}

if (-not (Test-Path $appPath)) {
    throw "Executable not found: $appPath`nPlease set appPath in server-config.json."
}

if (-not (Test-Path $dbPath)) {
    New-Item -ItemType Directory -Path $dbPath | Out-Null
    Write-Host "Created: $dbPath"
}

Write-Host "Starting DNA Server..."
Write-Host "  Executable : $appPath"
Write-Host "  Database   : $dbPath"
Write-Host "  Port       : $port"
Write-Host ""

& $appPath --db "$dbPath" --port $port
