# Agentic OS

**面向 AI Agent 的本地项目知识运行时。**

Agentic OS 让 Agent 具备项目级认知能力：理解结构、依赖、约束、设计决策与经验沉淀，而不只是修改当前文件。

[English](README.md)

## 当前产品形态

当前仓库只保留并描述 **单 App 方案**：

- 单进程
- 单窗口
- 单心智
- 单生命周期

桌面 App 进程内仍会嵌入本地运行时，默认监听：

```text
http://127.0.0.1:5052
```

但这条本地 HTTP 表面主要用于：

- MCP
- CLI
- 兼容层

桌面内部的长期目标是优先通过进程内服务访问 `Dna.Workbench` 与未来的 `Dna.Agent`，而不是把本地 HTTP 当作内部主调用链。

## 当前与目标架构

### 当前实现

```text
App
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

### 目标架构

```text
App
  ->
Dna.Agent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

说明：

- `Dna.Agent`
  - 负责内置 Agent 的编排、执行、模型调用与工具调用策略
- `Dna.Workbench`
  - 负责为内置 Agent、外置 Agent、CLI、桌面宿主提供统一项目能力
- `Dna.Knowledge`
  - 负责 Workspace / TopoGraph / Memory / Governance

## 运行拓扑

```text
用户
  |
  +--> Agentic OS App 桌面宿主（单窗口）
         - 项目加载
         - 工作区浏览
         - 知识图谱预览
         - 模块知识编辑
         - 记忆查看
         - 内置 chat / 未来内置 agent
         - 外置 Agent 接入
         - 进程内本地运行时 :5052
                |
                +--> MCP
                +--> agentic-os cli
                +--> 兼容 API
```

## Workbench 与 Agent 的分工

这是当前最重要的架构边界：

- `Dna.Agent`
  - 负责“怎么做”
  - 包括任务规划、步骤推进、执行循环、模型交互、工具选择
- `Dna.Workbench`
  - 负责“在这个项目里能做什么”
  - 包括工作区、知识图谱、模块知识、记忆、运行时观测等统一能力

因此：

- 内置 Agent 通过 `Dna.Agent -> Dna.Workbench` 工作
- 外置 Agent 通过 `MCP / CLI / HTTP Adapter -> Dna.Workbench` 工作

外置 Agent 自身已有编排能力，不需要依赖 `Dna.Agent` 才能工作；它们真正需要的是 `Dna.Workbench` 提供的项目能力面。

## 快速开始

### 1. 构建

```bash
dotnet build src/App/App.csproj
```

### 2. 启动桌面 App

开发模式：

```bash
dotnet run --no-launch-profile --project src/App
```

发布产物：

```bash
publish/agentic-os.exe
```

### 3. 准备项目目录

桌面 App 可以直接加载任意项目目录。

说明：

- 如果目录下已经存在 `.agentic-os/`，App 会直接复用
- 如果目录下还没有 `.agentic-os/`，App 会在首次加载时自动创建
- 只有在桌面 App 成功加载项目后，本地 `5052` 运行时才真正对外可用

### 4. 接入 Cursor / Codex

桌面 App 加载项目后，把 IDE 的 MCP 配置指向：

```json
{
  "mcpServers": {
    "agentic-os": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

## 项目级状态目录

当前 App 主要把项目知识与本地运行时相关文件放在 `.agentic-os/` 下：

- `knowledge/`
  - 知识图谱真相源
- `memory/`
  - 长期记忆
- `session/`
  - 短期工作记忆
- `logs/`
  - App 日志

## App 本地运行时表面

当前嵌入的本地运行时仍暴露：

- `/mcp`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/api/app/status`
- `/api/app/workspaces/*`
- `/api/app/tooling/*`
- `/agent/*`

其中：

- `/mcp`
  - 面向 IDE Agent
- `/api/*`
  - 当前兼容层与本地工具表面
- `/agent/*`
  - 仍属过渡接口，后续应由 `Dna.Agent` 收口

## CLI

当前桌面 App 自带本地 CLI 入口：

```bash
agentic-os cli status
agentic-os cli topology
agentic-os cli search render
agentic-os cli recall "有哪些约束"
agentic-os cli memories
agentic-os cli tools
```

默认本地运行时地址：

```text
http://127.0.0.1:5052
```

## 架构文档

详见：

- [ROADMAP.md](ROADMAP.md)
- [src/Dna.Agent/ARCHITECTURE.md](src/Dna.Agent/ARCHITECTURE.md)
- [src/Dna.Workbench/ARCHITECTURE.md](src/Dna.Workbench/ARCHITECTURE.md)
- [src/Dna.Knowledge/ARCHITECTURE.md](src/Dna.Knowledge/ARCHITECTURE.md)

## 许可证

[Apache 2.0](LICENSE)
