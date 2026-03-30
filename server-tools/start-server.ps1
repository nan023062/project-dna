param(
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot "..\server-config.json"
}

if (-not (Test-Path $ConfigPath)) {
    throw "Config file not found: $ConfigPath`nPlease fill config.json before running."
}

try {
    $configRaw = Get-Content -Path $ConfigPath -Raw -Encoding UTF8
    $config = $configRaw | ConvertFrom-Json
}
catch {
    throw "Config file is invalid: $ConfigPath`nPlease check JSON format."
}

if ($null -eq $config.server) {
    throw "config.json missing 'server' section."
}

$mode = $config.server.mode
$appPath = $config.server.appPath
$dbPath = $config.server.dbPath
$port = $config.server.port

if ([string]::IsNullOrWhiteSpace($appPath)) { throw "server.appPath is required in config.json" }
if ([string]::IsNullOrWhiteSpace($dbPath)) { throw "server.dbPath is required in config.json" }
if ($null -eq $port -or $port -le 0) { $port = 5051 }

$workspace = (Get-Location).Path

# 解析 dbPath 为绝对路径 (如果是相对路径，则相对于当前工作区)
if (-not [System.IO.Path]::IsPathRooted($dbPath)) {
    $dbPath = Join-Path $workspace $dbPath
}

if (-not (Test-Path $dbPath)) {
    New-Item -ItemType Directory -Path $dbPath | Out-Null
    Write-Host "Created DB directory: $dbPath"
}

Write-Host "Starting Project DNA Server..."
Write-Host "Mode    : $mode"
Write-Host "App Path: $appPath"
Write-Host "DB Path : $dbPath"
Write-Host "Port    : $port"
Write-Host "----------------------------------------"

if ($mode -eq "source") {
    # 如果是源码模式，appPath 应该是 Server 项目的路径
    if (-not [System.IO.Path]::IsPathRooted($appPath)) {
        $appPath = Join-Path $workspace $appPath
    }
    
    if (-not (Test-Path $appPath)) {
        throw "Source project not found at: $appPath"
    }
    
    Write-Host "Running via dotnet run..."
    dotnet run --project "$appPath" -- --db "$dbPath" --port $port
}
else {
    # 默认 binary 模式
    # 如果是相对路径且不是全局命令，尝试解析为绝对路径
    if (-not [System.IO.Path]::IsPathRooted($appPath) -and (Test-Path (Join-Path $workspace $appPath))) {
        $appPath = Join-Path $workspace $appPath
    }
    
    # 检查是否是全局命令或存在的路径
    $commandExists = (Get-Command $appPath -ErrorAction SilentlyContinue) -ne $null
    if (-not $commandExists -and -not (Test-Path $appPath)) {
        throw "Executable not found: $appPath. Please check server.appPath in config.json."
    }
    
    Write-Host "Running binary..."
    & $appPath --db "$dbPath" --port $port
}
