# Project DNA - Cursor 接入指南

Project DNA 通过 MCP (Model Context Protocol) 与 Cursor 集成，让 AI 助手具备项目级上下文与记忆能力。

本指南会把 Cursor 连接到 **本地 App MCP**（默认 `5052`）。

## 目录
- [前提条件](#前提条件)
- [方式一：使用自动化 Skill 接入（推荐）](#方式一使用自动化-skill-接入推荐)
- [方式二：手动配置接入](#方式二手动配置接入)
- [验证连接](#验证连接)
- [访问控制台](#访问控制台)
- [使用说明](#使用说明)

---

## 前提条件

1. 已安装 [Cursor IDE](https://cursor.sh/)。
2. 已启动 Agentic OS（本地 MCP 宿主，默认 `5052`）：
   ```bash
   dotnet run --no-launch-profile --project src/App
   ```

---

## 方式一：使用自动化 Skill 接入（推荐）

本目录提供了 Cursor Skill，可一键下发 MCP 配置与规则模板。

### 1. 复制 Skill 目录
将 `tools/dna-for-cursor` 复制到您项目的 `.cursor/skills/project-dna` 目录下。

### 2. 修改配置文件
打开 `.cursor/skills/project-dna/config.json`，按您的 **App 地址** 修改：

```json
{
  "app": {
    "serverIp": "127.0.0.1",
    "port": 5052,
    "serverName": "project-dna",
    "hook": {
      "enabled": true,
      "replaceExisting": true,
      "ruleFileName": "project-dna-mcp-hook.mdc",
      "agentFileName": "project-dna-mcp-hooks.md"
    }
  }
}
```

### 3. 执行安装脚本
在 Cursor 终端运行：

Windows (PowerShell)：
```powershell
powershell -ExecutionPolicy Bypass -File .\.cursor\skills\project-dna\scripts\install-app.ps1
```

macOS / Linux (Bash)：
```bash
bash .cursor/skills/project-dna/scripts/install-app.sh
```

脚本会自动：
- 生成或更新 `.cursor/mcp.json`
- 复制 DNA Prompt 规则到 `.cursor/rules/` 与 `.cursor/agents/`

### 4. 重启 Cursor
配置完成后，请完全重启 Cursor IDE 使 MCP 配置生效。

---

## 方式二：手动配置接入

如果不使用自动化脚本，也可手工配置。

### 1. 配置 MCP 连接
在项目根目录创建或编辑 `.cursor/mcp.json`：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://127.0.0.1:5052/mcp"
    }
  }
}
```

### 2. 引入工作流规则
建议将 `templates/rules/project-dna-mcp-hook.mdc` 复制到项目 `.cursor/rules/` 目录。

---

## 验证连接

1. 重启 Cursor 后，在 Settings -> Features -> MCP 查看 `project-dna` 是否为 `Connected`。
2. 应能看到 `get_context`、`recall`、`remember` 等工具。
3. 可额外检查 App 状态：
   ```bash
   curl http://127.0.0.1:5052/api/app/status
   ```

## 访问控制台

- App 控制台（本地运行时）：`http://127.0.0.1:5052`

---

## 使用说明

接入后，您可以在 Cursor Chat / Composer 中直接使用 DNA 能力：

1. 获取上下文：修改代码前调用 `get_context()`
2. 检索知识：不确定时调用 `recall("问题")`
3. 沉淀决策：确定方案后调用 `remember()`

提示：您也可以直接告诉 Cursor “把我们刚讨论的方案记录到 DNA”，AI 会自动做结构化落库。
