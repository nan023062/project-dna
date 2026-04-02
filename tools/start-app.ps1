$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$appProject = Join-Path $repoRoot "src\App\App.csproj"
$appDll = Join-Path $repoRoot "publish\agentic-os.dll"

if (-not (Test-Path $appProject)) {
    throw "App project was not found: $appProject"
}

if (-not (Test-Path $appDll)) {
    Write-Host "[tools] App binary not found. Building first..." -ForegroundColor Yellow
    dotnet build $appProject | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "App build failed."
    }
}

if (-not (Test-Path $appDll)) {
    throw "App binary was not found after build: $appDll"
}

Write-Host "[tools] Starting App..." -ForegroundColor Cyan
Write-Host "[tools] Repo:   $repoRoot"
Write-Host "[tools] Binary: $appDll"

$process = Start-Process -FilePath "dotnet" -ArgumentList @($appDll) -WorkingDirectory $repoRoot -PassThru

Start-Sleep -Milliseconds 800

if ($process.HasExited) {
    throw "App exited immediately. ExitCode=$($process.ExitCode)"
}

Write-Host "[tools] App started. PID=$($process.Id)" -ForegroundColor Green
