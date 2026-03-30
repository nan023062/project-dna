# Project DNA - Cursor 接入指南

Project DNA 提供了标准的 MCP (Model Context Protocol) 接口，可以无缝接入 Cursor IDE，让您的 AI 编程助手拥有项目级的记忆和认知能力。

本指南将帮助您快速在 Cursor 中配置并使用 Project DNA。

## 目录
- [前提条件](#前提条件)
- [方式一：使用自动化 Skill 接入（推荐）](#方式一使用自动化-skill-接入推荐)
- [方式二：手动配置接入](#方式二手动配置接入)
- [验证连接](#验证连接)
- [使用说明](#使用说明)

---

## 前提条件

1. 已安装 [Cursor IDE](https://cursor.sh/)。
2. 已经部署并运行了 Project DNA Server（本地或远程）。
   - *如果您还未启动 Server，请参考主仓库的启动文档，或运行 `dotnet run --project src/Server`。*

---

## 方式一：使用自动化 Skill 接入（推荐）

我们在本目录下提供了一套 Cursor Skill，可以一键完成 MCP 配置和规则模板的下发。

### 1. 复制 Skill 目录
将本目录（`client-install-guide/dna-for-cursor`）复制到您当前开发项目的 `.cursor/skills/project-dna` 目录下。

### 2. 修改配置文件
打开 `.cursor/skills/project-dna/config.json`，根据您的 DNA Server 实际地址修改配置：
```json
{
  "serverIp": "127.0.0.1",
  "port": 5051,
  "serverName": "project-dna-mcp"
}
```

### 3. 执行安装脚本
在 Cursor 的终端中，运行以下脚本：

**Windows (PowerShell)**:
```powershell
powershell -ExecutionPolicy Bypass -File .\.cursor\skills\project-dna\scripts\install-client.ps1
```

**macOS / Linux (Bash)**:
```bash
bash .cursor/skills/project-dna/scripts/install-client.sh
```
该脚本会自动：
- 在您的项目中生成或更新 `.cursor/mcp.json`。
- 将 DNA 相关的 Prompt 规则模板复制到 `.cursor/rules/` 和 `.cursor/agents/` 目录中。

### 4. 重启 Cursor
配置完成后，请完全重启 Cursor IDE 以使 MCP 配置生效。

---

## 方式二：手动配置接入

如果您不想使用自动化脚本，也可以手动配置 Cursor。

### 1. 配置 MCP 连接
在您的项目根目录创建或编辑 `.cursor/mcp.json` 文件，添加以下内容（请替换为您实际的 Server URL）：

```json
{
  "mcpServers": {
    "project-dna-mcp": {
      "command": "curl",
      "args": [
        "-s",
        "-X",
        "POST",
        "-H",
        "Content-Type: application/json",
        "-d",
        "{}",
        "http://127.0.0.1:5051/mcp"
      ]
    }
  }
}
```
*(注：具体 command 和 args 根据您的操作系统和网络环境可能有所不同，通常推荐使用 SSE 或 stdio 桥接工具)*

### 2. 引入工作流规则
为了让 Cursor 的 AI 知道如何使用 DNA MCP，建议将 `templates/rules/project-dna-mcp-hook.mdc` 复制到您项目的 `.cursor/rules/` 目录下。

---

## 验证连接

重启 Cursor 后，打开 Cursor 的设置 (Settings) -> Features -> MCP 选项卡。
如果您能看到名为 `project-dna-mcp` 的服务器状态为绿色的 `Connected`，并且能列出 `get_context`, `recall`, `remember` 等工具，说明连接成功！

---

## 使用说明

接入成功后，您可以在 Cursor 的 Chat 或 Composer 中直接和 AI 交互，AI 将自动调用 Project DNA 的能力：

1. **获取上下文**：AI 在修改代码前，会自动调用 `get_context()` 获取当前模块的架构和约束。
2. **检索知识**：遇到不确定的设计时，AI 会调用 `recall("问题")` 搜索项目知识库。
3. **沉淀决策**：当您和 AI 讨论并确定了新的架构方案或业务逻辑时，AI 会调用 `remember()` 将这些决策永久记录到 DNA 知识库中。

> **提示**：您也可以直接对 Cursor 说：“请把我们刚才讨论的数据库表结构设计记录到 DNA 中”，AI 会自动为您完成知识的结构化存储。
