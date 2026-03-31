# Project DNA - Codex 接入指南

Project DNA 通过 MCP (Model Context Protocol) 与 Codex 集成，让 AI 助手具备项目级上下文与记忆能力。

本指南会把 Codex 连接到 **本地 Client MCP**（默认 `5052`），再由 Client 代理到共享 Server（默认 `5051`）。

## 目录
- [前提条件](#前提条件)
- [方式一：使用自动化工具接入（推荐）](#方式一使用自动化工具接入推荐)
- [方式二：手动配置接入](#方式二手动配置接入)
- [验证连接](#验证连接)
- [访问控制台](#访问控制台)

---

## 前提条件

1. 已安装 Codex CLI / Codex IDE。
2. 已启动 Project DNA Server（团队共享，默认 `5051`）：
   ```bash
   dotnet run --project src/Server -- --db <知识库目录> --port 5051
   ```
3. 已启动 Project DNA Client（本地 MCP 宿主，默认 `5052`）：
   ```bash
   dotnet run --project src/Client -- --server http://127.0.0.1:5051 --port 5052
   ```

---

## 方式一：使用自动化工具接入（推荐）

本目录提供了一套 Codex 工具，可一键生成 MCP 配置与提示模板。

### 1. 复制工具目录
将 `client-tools/dna-for-codex` 复制到您项目的 `.codex/skills/project-dna` 目录下。

### 2. 修改配置文件
打开 `.codex/skills/project-dna/config.json`，按您的 **Client 地址** 修改：

```json
{
  "client": {
    "serverIp": "127.0.0.1",
    "port": 5052,
    "serverName": "project-dna",
    "hook": {
      "enabled": true,
      "replaceExisting": true,
      "promptFileName": "project-dna-mcp-hook.md",
      "agentFileName": "project-dna-mcp-hooks.md"
    }
  }
}
```

### 3. 执行安装脚本

Windows (PowerShell)：
```powershell
powershell -ExecutionPolicy Bypass -File .\.codex\skills\project-dna\scripts\install-client.ps1
```

macOS / Linux (Bash)：
```bash
bash .codex/skills/project-dna/scripts/install-client.sh
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
    "project-dna": {
      "url": "http://127.0.0.1:5052/mcp"
    }
  }
}
```

---

## 验证连接

1. 重启 Codex 会话后，确认 MCP `project-dna` 已连接。
2. 应能看到 `get_context`、`recall`、`remember` 等工具。
3. 可额外检查 Client 状态：
   ```bash
   curl http://127.0.0.1:5052/api/client/status
   ```

## 访问控制台

- Client 控制台（本地）：`http://127.0.0.1:5052`
- Server 管理台（共享服务）：`http://127.0.0.1:5051`
