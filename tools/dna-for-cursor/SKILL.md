---
name: agentic-os
description: Agentic OS 桌面应用接入工具。用于用户要求“启动桌面应用”“安装桌面应用”“连接 MCP”或“打开 Dashboard”时。
---

# 技能：Agentic OS 桌面应用工具箱

这是 Agentic OS 的桌面应用管理入口，负责桌面应用的快速接入。

## 触发时机

**场景 A：桌面应用接入**
- 用户说“启动桌面应用”
- 用户说“安装桌面应用”
- 用户说“连接 MCP”
- 用户说“配置 mcp.json”

**场景 B：访问 Dashboard**
- 用户说“打开控制台”
- 用户说“打开 Dashboard”
- 用户说“查看知识图谱”

## 统一配置源

- 配置文件：`.cursor/skills/agentic-os/config.json`
- 桌面应用读取此配置（`serverIp`、`port`、`serverName` 等），用于连接本地 `App MCP`。
- **如果用户要执行任何操作，必须先检查此文件是否已填写完整。若有空值，提示用户填写。**

## 执行步骤：启动桌面应用（接入 MCP）

1. **检查配置**：确保 `config.json` 必填项不为空。
2. **执行安装**：
   - **Windows**:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\.cursor\skills\agentic-os\scripts\install-app.ps1
     ```
   - **macOS / Linux**:
     ```bash
     bash .cursor/skills/agentic-os/scripts/install-app.sh
     ```
3. **验证结果**：
   - 检查 `.cursor/mcp.json` 是否已生成。
   - 检查 `.cursor/rules/` 和 `.cursor/agents/` 下的 Hook 模板是否已就位。
   - 尝试 `curl http://<config.app.serverIp>:<config.app.port>/api/app/status` 验证连通性。
4. **提示用户**：告知已完成配置，请重启 Cursor。并提醒用户可访问 `http://<config.app.serverIp>:<config.app.port>` 查看本地 App 控制台。

## 执行步骤：打开 Dashboard

1. **读取配置**：读取 `config.json` 中的 `app.serverIp` 和 `app.port`。
2. **拼接 URL**：`http://<serverIp>:<port>`。
3. **打开浏览器**：
   - **Windows**:
     ```powershell
     Start-Process "http://<serverIp>:<port>"
     ```
   - **macOS**:
     ```bash
     open "http://<serverIp>:<port>"
     ```
   - **Linux**:
     ```bash
     xdg-open "http://<serverIp>:<port>"
     ```

## 常见问题

- **配置文件为空**：脚本会阻断执行，请用户先填写 `.cursor/skills/agentic-os/config.json`。
- **连接失败**：检查服务器防火墙是否放通了对应端口。
