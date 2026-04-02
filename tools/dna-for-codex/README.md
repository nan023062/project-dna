# Agentic OS - Codex 接入指南

Agentic OS 通过 MCP (Model Context Protocol) 与 Codex 集成，让 AI 助手具备项目级上下文与记忆能力。

本指南会把 Codex 连接到 **本地 App MCP**（默认 `5052`）。

## 目录
- [前提条件](#前提条件)
- [方式一：使用自动化工具接入（推荐）](#方式一使用自动化工具接入推荐)
- [方式二：手动配置接入](#方式二手动配置接入)
- [验证连接](#验证连接)
- [访问控制台](#访问控制台)

---

## 前提条件

1. 已安装 Codex CLI / Codex IDE。
2. 已启动 Agentic OS（本地 MCP 宿主，默认 `5052`）：
   ```bash
   dotnet run --no-launch-profile --project src/App
   ```

---

## 方式一：使用自动化工具接入（推荐）

本目录提供了一套 Codex 工具，可一键生成 MCP 配置与提示模板。

### 1. 复制工具目录
将 `tools/dna-for-codex` 复制到您项目的 `.codex/skills/agentic-os` 目录下。

### 2. 修改配置文件
打开 `.codex/skills/agentic-os/config.json`，按您的 **App 地址** 修改：

```json
{
  "app": {
    "serverIp": "127.0.0.1",
    "port": 5052,
    "serverName": "agentic-os",
    "hook": {
      "enabled": true,
      "replaceExisting": true,
      "promptFileName": "agentic-os-mcp-hook.md",
      "agentFileName": "agentic-os-mcp-hooks.md"
    }
  }
}
```

### 3. 执行安装脚本

Windows (PowerShell)：
```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\agentic-os\scripts\install-app.ps1
```

macOS / Linux (Bash)：
```bash
bash .codex/skills/agentic-os/scripts/install-app.sh
```

脚本会自动：
- 生成或更新 `.codex/mcp.json`
- 生成或更新 `.codex/prompts/<promptFileName>`
- 生成或更新 `.codex/agents/<agentFileName>`

### 4. 重启 Codex 会话
配置完成后，请重启 Codex 会话使 MCP 配置生效。

---

## 方式二：手动配置接入

在项目根目录创建或编辑 `.codex/mcp.json`：

```json
{
  "mcpServers": {
    "agentic-os": {
      "url": "http://127.0.0.1:5052/mcp"
    }
  }
}
```

---

## 验证连接

1. 重启 Codex 会话后，确认 MCP `agentic-os` 已连接。
2. 应能看到 `get_context`、`recall`、`remember` 等工具。
3. 可额外检查 App 状态：
   ```bash
   curl http://127.0.0.1:5052/api/app/status
   ```

## 访问控制台

- App 控制台（本地运行时）：`http://127.0.0.1:5052`
