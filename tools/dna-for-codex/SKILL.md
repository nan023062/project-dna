---
name: project-dna-codex
description: Project DNA 的 Codex 桌面应用接入工具。用于“配置 Codex MCP”“安装 codex 工具”或“打开控制台”场景。
---

# 技能：Project DNA Codex 接入工具箱

这是 Project DNA 的 Codex 接入入口，负责将 Codex 连接到本地 App MCP。

## 触发时机

- 用户说“配置 codex mcp”
- 用户说“安装 codex 工具”
- 用户说“连接 project dna 到 codex”
- 用户说“打开 app 控制台”

## 统一配置源

- 配置文件：`.codex/skills/project-dna/config.json`
- 必填字段：`app.serverIp`、`app.port`、`app.serverName`、`app.hook.*`

## 执行步骤

1. 检查配置文件必填项。
2. 运行安装脚本：
   - Windows：
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\.codex\skills\project-dna\scripts\install-app.ps1
     ```
   - macOS / Linux：
     ```bash
     bash .codex/skills/project-dna/scripts/install-app.sh
     ```
3. 验证生成结果：
   - `.codex/mcp.json`
   - `.codex/prompts/` 下提示模板
   - `.codex/agents/` 下 agent 模板
4. 连通性检查：
   - `curl http://<config.app.serverIp>:<config.app.port>/api/app/status`
5. 提示用户重启 Codex 会话。

## 常见问题

- 配置为空：脚本会直接报错并阻断。
- MCP 不连接：先检查 App 是否启动、端口是否正确、网络是否可达。
