# Project DNA

**AI Agent 的项目认知引擎。**

Project DNA 让 Agent 具备项目级认知能力：理解结构、依赖、约束、历史决策和经验教训，而不是只会改当前文件。

[English](README.md)

## 当前形态

Project DNA 现在采用 **Server + Client 桌面宿主** 的拆分方式：

- `Server` 是共享知识服务，负责知识图谱、记忆、治理能力以及管理台。
- `Client` 是 **单进程、单窗口** 的桌面宿主，负责加载项目、预览图谱和知识，并向 IDE Agent 暴露本地 MCP。
- 本地 `5052` 只是 `Client` 进程内嵌的 API / MCP 接入面，不是第二个产品，也不是独立浏览器工作台。

```text
Cursor / Codex / 其他 IDE Agent
                |
                | MCP
                v
Project DNA Client 桌面宿主（单窗口）
                |
                | 进程内嵌本地 API / MCP :5052
                |
                +------> 桌面交互界面
                |
                +------> REST 到 Server
                           |
                           v
Project DNA Server (:5051)
  - 知识图谱
  - 记忆存储
  - 治理能力
  - Server 管理台（wwwroot）
```

## 当前 MVP 口径

当前 MVP 先聚焦 **单人本地管理员闭环**：

- `Server` 管理台作为主要管理入口
- `Client` 桌面宿主作为主要本地工作入口
- 权限控制当前以 **Server 白名单 + 角色展示** 为主
- `Client` 当前主要提供正式知识预览、图谱预览和本地 MCP 接入
- 正式知识直写目前只对 `admin` 开放
- 团队协作版的评审流、JWT 等能力仍保留在仓库设计中，但不是当前 MVP 的主运行路径

## 快速开始

### 1. 编译

```bash
cd src
dotnet build
```

### 2. 启动 Server

`--db` 为必填，指向知识库目录，内部使用 SQLite 存储。

```bash
# 当前目录作为知识库目录
cd /path/to/knowledge-store
dna --db

# 或显式指定目录
dna --db /path/to/knowledge-store
```

默认端口：`5051`

### 3. 启动 Client 桌面宿主

```bash
dotnet run --no-launch-profile --project src/Client
```

当前 Client 行为：

- 打开一个桌面主窗口
- 选择一个包含 `.project.dna/project.json` 的项目目录
- 在项目加载成功后，于同一进程内启动 `http://127.0.0.1:5052`
- 让“窗口生命周期”和“本地 MCP/API 生命周期”保持一致

`.project.dna/project.json` 示例：

```json
{
  "projectName": "agentic-os",
  "serverBaseUrl": "http://127.0.0.1:5051"
}
```

客户端项目日志写入 `.project.dna/logs/`。
客户端工作区状态写入 `.project.dna/client-workspaces.json`。
客户端本地 agent shell 状态写入 `.project.dna/agent-shell/agent-shell-state.json`。

### 4. 接入 Cursor / Codex

先完成下面三步：

1. 启动 `Server`
2. 启动桌面 `Client`
3. 在桌面窗口中加载目标项目

然后把 IDE 的 MCP 配置指向：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

说明：

- 只有在桌面 `Client` 成功加载项目后，`5052` 才会在线。
- IDE 连接的是桌面 `Client`，不是直接连接 `Server`。

## 运行入口

### Server 管理台

浏览器打开 `http://localhost:5051`

当前重点界面：

- 服务概览
- 连接权限 / 白名单管理
- 审核队列基础页
- 图谱与记忆管理

### Client 桌面宿主

当前 `Client` 只保留一个对用户可见的桌面运行形态。

当前能力包括：

- 项目选择与最近项目列表
- 服务状态与连接权限概览
- 图谱预览
- 正式知识预览
- 本地 MCP 接入中心
- 一键安装 Cursor / Codex 接入配置

当前实现中 **不再提供独立的 Client 浏览器工作台**。

## CLI

当前保留的是 `Server` 侧运维 / 查询 CLI：

```bash
dna cli status
dna cli validate
dna cli search combat
dna cli recall "有哪些约束"
dna cli stats
```

## Client 本地运行面

桌面 `Client` 进程内嵌的本地运行面当前包括：

- MCP 入口：`/mcp`
- 桌面宿主配套接口：`/api/client/status`、`/api/client/workspaces/*`、`/api/client/tooling/*`
- 上游查询 / 代理接口：`/api/status`、`/api/topology`、`/api/connection/access`、`/api/memory/*`
- 本地轻量 Agent Shell：`/agent/*`

这些接口是为了支撑桌面宿主与 IDE 接入，不代表存在第二套独立 Client Web 产品。

## MCP 工具

### 图谱工具

| 工具 | 说明 |
|------|------|
| `get_topology` | 查看完整知识图谱 |
| `get_context` | 获取模块上下文、约束、依赖与经验 |
| `search_modules` | 按关键字搜索节点 |
| `get_dependency_order` | 多模块修改时的依赖排序 |
| `register_module` | 注册知识节点 |
| `register_crosswork` | 声明跨团队协作 |
| `validate_architecture` | 进行架构健康检查 |

### 记忆工具

| 工具 | 说明 |
|------|------|
| `remember` | 写入知识 |
| `recall` | 语义检索知识 |
| `batch_remember` | 批量写入 |
| `query_memories` | 结构化查询 |
| `get_memory` | 按 ID 获取记忆 |
| `get_memory_stats` | 获取知识库统计 |
| `verify_memory` | 确认知识是否仍有效 |
| `update_memory` | 更新记忆 |
| `delete_memory` | 删除记忆 |
| `condense_module_knowledge` | 将单模块知识压缩到 `NodeKnowledge` |
| `condense_all_module_knowledge` | 全量知识压缩 |

此外，`Client` 也提供 `GET /api/client/mcp/tools`，供桌面 UI 和自动化读取完整 MCP 工具清单。

## 架构说明

- `Server` 是知识图谱和记忆的唯一权威存储
- `Server` 不直接访问项目源码
- `Client` 是本地桌面宿主与 MCP 网关
- 团队版权限与审核链路后续会继续推进，但当前 MVP 以“单人本地管理员优先”收口

详细可见：

- [docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)
- [docs/architecture/project-dna-transport-auth-decision.md](docs/architecture/project-dna-transport-auth-decision.md)

## 许可协议

[Apache 2.0](LICENSE)
