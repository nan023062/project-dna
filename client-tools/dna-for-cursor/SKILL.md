---
name: project-dna
description: Project DNA 客户端接入工具。用于用户要求“启动客户端”“安装客户端”“连接 MCP”时。支持一键下发全套客户端配置。
---

# 技能：Project DNA 客户端工具箱

这是 Project DNA 的客户端管理入口，负责客户端的快速接入。

## 触发时机

**场景 A：客户端接入**
- 用户说“启动客户端”
- 用户说“安装客户端”
- 用户说“连接 MCP”
- 用户说“配置 mcp.json”

## 统一配置源

- 配置文件：`.cursor/skills/project-dna/config.json`
- 客户端读取此配置（`serverIp`、`port`、`serverName` 等）。
- **如果用户要执行任何操作，必须先检查此文件是否已填写完整。若有空值，提示用户填写。**

## 执行步骤：启动客户端（接入 MCP）

1. **检查配置**：确保 `config.json` 必填项不为空。
2. **执行安装**：
   - **Windows**:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .\.cursor\skills\project-dna\scripts\install-client.ps1
     ```
   - **macOS / Linux**:
     ```bash
     bash .cursor/skills/project-dna/scripts/install-client.sh
     ```
3. **验证结果**：
   - 检查 `.cursor/mcp.json` 是否已生成。
   - 检查 `.cursor/rules/` 和 `.cursor/agents/` 下的 Hook 模板是否已就位。
   - 尝试 `curl http://<config.client.serverIp>:<config.client.port>/api/status` 验证连通性。
4. **提示用户**：告知已完成配置，请重启 Cursor。并提醒用户可以通过浏览器访问 `http://<config.client.serverIp>:<config.client.port>` 查看可视化的 Web Dashboard。

## 常见问题

- **配置文件为空**：脚本会阻断执行，请用户先填写 `.cursor/skills/project-dna/config.json`。
- **连接失败**：检查服务器防火墙是否放通了对应端口。
