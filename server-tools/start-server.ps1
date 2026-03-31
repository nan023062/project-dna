$ErrorActionPreference = "Stop"

$configPath = Join-Path $PSScriptRoot "server-config.json"
if (-not (Test-Path $configPath)) {
    throw "Config not found: $configPath`nPlease copy server-config.example.json to server-config.json and edit it."
}

$config     = Get-Content $configPath -Raw | ConvertFrom-Json
$rawAppPath = [string]$config.appPath
$dbPath     = $config.dbPath
$port       = if ($config.port) { $config.port } else { 5051 }
$isCommand  = $false

if ([System.IO.Path]::IsPathRooted($rawAppPath)) {
    $appPath = $rawAppPath
}
elseif ($rawAppPath.Contains("/") -or $rawAppPath.Contains("\")) {
    $appPath = Join-Path $PSScriptRoot $rawAppPath
}
else {
    $appPath = $rawAppPath
    $isCommand = $true
}

if (-not [System.IO.Path]::IsPathRooted($dbPath)) {
    $dbPath = Join-Path (Get-Location).Path $dbPath
}

if ($isCommand) {
    $cmd = Get-Command $appPath -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $cmd) {
        throw "Executable command not found in PATH: $appPath`nPlease set appPath in server-config.json."
    }
    $resolvedAppPath = $cmd.Source
}
else {
    if (-not (Test-Path $appPath)) {
        # 兼容 Windows 场景：配置未写 .exe 但发布产物是 dna_server.exe
        if (-not $appPath.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
            $candidate = "$appPath.exe"
            if (Test-Path $candidate) {
                $appPath = $candidate
            }
        }
    }

    if (-not (Test-Path $appPath)) {
        throw "Executable not found: $appPath`nPlease set appPath in server-config.json."
    }

    $resolvedAppPath = $appPath
}

if (-not (Test-Path $dbPath)) {
    New-Item -ItemType Directory -Path $dbPath | Out-Null
    Write-Host "Created: $dbPath"
}

Write-Host "Starting DNA Server..."
Write-Host "  Executable : $resolvedAppPath"
Write-Host "  Database   : $dbPath"
Write-Host "  Port       : $port"
Write-Host ""

& $resolvedAppPath --db "$dbPath" --port $port
