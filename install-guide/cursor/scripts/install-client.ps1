param(
    [string]$ConfigPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot "..\config.json"
}

if (-not (Test-Path $ConfigPath)) {
    throw "Config file not found: $ConfigPath`nPlease fill config.json before running."
}

try {
    $configRaw = Get-Content -Path $ConfigPath -Raw -Encoding UTF8
    if ([string]::IsNullOrWhiteSpace($configRaw)) {
        throw "Config file is empty."
    }
    $config = $configRaw | ConvertFrom-Json
}
catch {
    throw "Config file is invalid: $ConfigPath`nPlease check JSON format and required fields."
}

$missingFields = @()
if ($null -eq $config.client) { throw "config.json missing 'client' section." }
if ($null -eq $config.client.serverIp -or [string]::IsNullOrWhiteSpace([string]$config.client.serverIp)) { $missingFields += "client.serverIp" }
if ($null -eq $config.client.port -or [int]$config.client.port -le 0) { $missingFields += "client.port" }
if ($null -eq $config.client.serverName -or [string]::IsNullOrWhiteSpace([string]$config.client.serverName)) { $missingFields += "client.serverName" }
if ($null -eq $config.client.hook) { $missingFields += "client.hook" }
if ($null -eq $config.client.hook.enabled) { $missingFields += "client.hook.enabled" }
if ($null -eq $config.client.hook.replaceExisting) { $missingFields += "client.hook.replaceExisting" }
if ($null -eq $config.client.hook.ruleFileName -or [string]::IsNullOrWhiteSpace([string]$config.client.hook.ruleFileName)) { $missingFields += "client.hook.ruleFileName" }
if ($null -eq $config.client.hook.agentFileName -or [string]::IsNullOrWhiteSpace([string]$config.client.hook.agentFileName)) { $missingFields += "client.hook.agentFileName" }

if ($missingFields.Count -gt 0) {
    $fields = $missingFields -join ", "
    throw "config.json has empty required values. Fill these fields first: $fields"
}

$effectiveServerIp = [string]$config.client.serverIp
$effectivePort = [int]$config.client.port
$effectiveServerName = [string]$config.client.serverName
$effectiveHookEnabled = [bool]$config.client.hook.enabled
$effectiveHookReplaceExisting = [bool]$config.client.hook.replaceExisting
$effectiveRuleFileName = [string]$config.client.hook.ruleFileName
$effectiveAgentFileName = [string]$config.client.hook.agentFileName

$workspace = (Get-Location).Path
$cursorDir = Join-Path $workspace ".cursor"
$rulesDir = Join-Path $cursorDir "rules"
$agentsDir = Join-Path $cursorDir "agents"
$mcpFile = Join-Path $cursorDir "mcp.json"
$ruleFile = Join-Path $rulesDir $effectiveRuleFileName
$agentFile = Join-Path $agentsDir $effectiveAgentFileName
$endpoint = "http://{0}:{1}/mcp" -f $effectiveServerIp, $effectivePort

Write-Host "Workspace : $workspace"
Write-Host "Config    : $ConfigPath"
Write-Host "MCP Name  : $effectiveServerName"
Write-Host "MCP URL   : $endpoint"
Write-Host "Hook      : $effectiveHookEnabled"

if (-not (Test-Path $cursorDir)) {
    New-Item -ItemType Directory -Path $cursorDir | Out-Null
    Write-Host "Created   : $cursorDir"
}

if (-not (Test-Path $rulesDir)) {
    New-Item -ItemType Directory -Path $rulesDir | Out-Null
    Write-Host "Created   : $rulesDir"
}

if (-not (Test-Path $agentsDir)) {
    New-Item -ItemType Directory -Path $agentsDir | Out-Null
    Write-Host "Created   : $agentsDir"
}

function Backup-File {
    param([string]$Path)
    if (Test-Path $Path) {
        $backupPath = "{0}.{1}.bak" -f $Path, (Get-Date -Format "yyyyMMddHHmmss")
        Copy-Item -Path $Path -Destination $backupPath -Force
        Write-Host "Backup    : $backupPath"
    }
}

Backup-File -Path $mcpFile

function Write-ManagedFile {
    param(
        [string]$Path,
        [string]$Content,
        [bool]$ReplaceExisting
    )

    if ((Test-Path $Path) -and -not $ReplaceExisting) {
        Write-Host "Skip      : $Path (exists and replaceExisting=false)"
        return
    }

    Backup-File -Path $Path
    Set-Content -Path $Path -Value $Content -Encoding UTF8
    Write-Host "Updated   : $Path"
}

function New-RuleContent {
    param([string]$Endpoint)
@"
---
description: Project DNA MCP Hook gate
globs: ["**/*"]
---

# Project DNA MCP Hook

Use the Project DNA MCP workflow before editing files.

1. Validate project identity first when identity tools are available.
2. Before file edits, fetch task context via begin_task/get_context.
3. If unsure about conventions, call recall first.
4. After completion, write completed-task memory; write lesson memory on issues.

Current MCP endpoint:
- $Endpoint

If MCP is unavailable, report it clearly and continue non-knowledge-tool work.
"@
}

function New-AgentContent {
    param([string]$Endpoint, [string]$ServerName)
@"
# Project DNA MCP Agent Hooks

Use this file as team default agent prompt guidance.

Always run MCP knowledge workflow before editing files.

- MCP Server: $ServerName
- MCP Endpoint: $Endpoint

Dialog hooks:
1. Session start: validate project identity first when tools exist.
2. Task start: run begin_task/get_context (search modules if unknown).
3. During task: call recall before uncertain decisions.
4. Important decisions: remember with #decision.
5. Task end: remember with #completed-task and #lesson when needed.
"@
}

$finalConfig = @{
    mcpServers = @{
        $effectiveServerName = @{
            command = "curl"
            args = @("-s", "-X", "POST", "-H", "Content-Type: application/json", "-d", "{}", $endpoint)
        }
    }
}

if (Test-Path $mcpFile) {
    try {
        $existingRaw = Get-Content -Path $mcpFile -Raw -Encoding UTF8
        if (-not [string]::IsNullOrWhiteSpace($existingRaw)) {
            $existing = $existingRaw | ConvertFrom-Json
            if ($null -ne $existing -and $null -ne $existing.mcpServers) {
                $mergedServers = @{}
                foreach ($prop in $existing.mcpServers.PSObject.Properties) {
                    $mergedServers[$prop.Name] = $prop.Value
                }
                $mergedServers[$effectiveServerName] = @{
                    command = "curl"
                    args = @("-s", "-X", "POST", "-H", "Content-Type: application/json", "-d", "{}", $endpoint)
                }
                $finalConfig = @{ mcpServers = $mergedServers }
            }
        }
    }
    catch {
        Write-Warning "Existing mcp.json parse failed, fallback to overwrite."
    }
}

$json = $finalConfig | ConvertTo-Json -Depth 20
Set-Content -Path $mcpFile -Value $json -Encoding UTF8

if ($effectiveHookEnabled) {
    $ruleContent = New-RuleContent -Endpoint $endpoint
    $agentContent = New-AgentContent -Endpoint $endpoint -ServerName $effectiveServerName

    $templateRulesDir = Join-Path $PSScriptRoot "..\templates\rules"
    $templateAgentsDir = Join-Path $PSScriptRoot "..\templates\agents"

    if (Test-Path (Join-Path $templateRulesDir $effectiveRuleFileName)) {
        $ruleContent = Get-Content -Path (Join-Path $templateRulesDir $effectiveRuleFileName) -Raw -Encoding UTF8
        # 替换端点占位符
        $ruleContent = $ruleContent -replace "\{\{MCP_ENDPOINT\}\}", $endpoint
    }
    
    if (Test-Path (Join-Path $templateAgentsDir $effectiveAgentFileName)) {
        $agentContent = Get-Content -Path (Join-Path $templateAgentsDir $effectiveAgentFileName) -Raw -Encoding UTF8
        # 替换端点占位符
        $agentContent = $agentContent -replace "\{\{MCP_ENDPOINT\}\}", $endpoint
    }

    Write-ManagedFile -Path $ruleFile -Content $ruleContent -ReplaceExisting $effectiveHookReplaceExisting
    Write-ManagedFile -Path $agentFile -Content $agentContent -ReplaceExisting $effectiveHookReplaceExisting

    # 复制 templates/rules 下的其他规则
    if (Test-Path $templateRulesDir) {
        Get-ChildItem -Path $templateRulesDir -Filter "*.mdc" | Where-Object { $_.Name -ne $effectiveRuleFileName } | ForEach-Object {
            $targetPath = Join-Path $rulesDir $_.Name
            $content = Get-Content -Path $_.FullName -Raw -Encoding UTF8
            Write-ManagedFile -Path $targetPath -Content $content -ReplaceExisting $effectiveHookReplaceExisting
        }
    }

    # 复制 templates/agents 下的其他 Agent 提示
    if (Test-Path $templateAgentsDir) {
        Get-ChildItem -Path $templateAgentsDir -Filter "*.md" | Where-Object { $_.Name -ne $effectiveAgentFileName } | ForEach-Object {
            $targetPath = Join-Path $agentsDir $_.Name
            $content = Get-Content -Path $_.FullName -Raw -Encoding UTF8
            Write-ManagedFile -Path $targetPath -Content $content -ReplaceExisting $effectiveHookReplaceExisting
        }
    }
}

Write-Host ""
Write-Host "Done. Cursor MCP and hook config updated:"
Write-Host "  $mcpFile"
if ($effectiveHookEnabled) {
    Write-Host "  $ruleFile"
    Write-Host "  $agentFile"
}
Write-Host ""
Write-Host "Next steps:"
Write-Host "1) Restart Cursor"
Write-Host "2) Check MCP panel: '$effectiveServerName' should be connected"
Write-Host "3) Optional verify: $endpoint"
