# Project DNA

**AI Agent 的知识引擎。**

Mem0 让 Agent 记住对话，GitNexus 让 Agent 看懂代码语法，CLAUDE.md 存个人备忘。
**Project DNA 让 AI Agent 理解整个项目**——结构、知识、依赖、约束和跨团队协作——让 Agent 带着完整上下文精准修改文件。

[English](README.md)

---

## 快速开始

### 1. 编译

```bash
cd src
dotnet build
```

### 2. 运行

```bash
# 先启动知识服务器（仅知识库 / API / Dashboard）
cd /data/dna/my-project && dna --db         # 当前目录作为知识库
dna --db /data/dna/my-project               # 指定知识库路径
dna --db /data/dna/my-project --port 5051   # 指定端口（示例）

# 再启动客户端（MCP + 决策执行入口）
Client --server http://localhost:5051 --port 5052
Client --stdio --server http://localhost:5051   # stdio 模式（由 IDE 启动）
```

`--db` 必须指定，指向知识库存储目录。
Server 不访问项目源码，只负责知识数据一致性与多端共享；Client 承载 MCP 与决策执行能力。

### 3. 接入 IDE

`.cursor/mcp.json` 或 `.codex/mcp.json`：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

推荐保持固定拓扑：`Server(5051) + Client(5052)`。远程连接时配置 Client 的地址。

### 4. Dashboard

浏览器打开 `http://localhost:5051`：
- 架构拓扑图浏览（只读）
- 记忆增删查改
- LLM 配置 + AI 对话

### 5. CLI

```bash
dna cli status                          # 服务状态
dna cli validate                        # 架构健康检查
dna cli search combat                   # 搜索模块
dna cli recall "有什么约束"              # 语义检索记忆
dna cli stats                           # 知识库统计
```

### 6. 客户端执行管线（架构师 -> 开发者）

客户端内置可配置执行管线，默认顺序为“先复盘再开发”：

- 读取配置：`GET /api/client/pipeline/config`
- 更新配置：`PUT /api/client/pipeline/config`
- 执行管线：`POST /api/client/pipeline/run`
- 最近结果：`GET /api/client/pipeline/runs/latest`

## MCP 工具

### 图谱工具

| 工具 | 说明 |
|------|------|
| `get_topology` | 查看知识图谱全貌 |
| `get_context` | 获取模块上下文（约束、依赖、CrossWork、教训） |
| `search_modules` | 按关键词搜索模块 |
| `get_dependency_order` | 多模块依赖排序 |
| `register_module` | 注册模块 |
| `register_crosswork` | 声明跨模块协作 |
| `validate_architecture` | 架构健康检查 |

### 记忆工具

| 工具 | 说明 |
|------|------|
| `remember` | 写入知识 |
| `recall` | 语义检索 |
| `batch_remember` | 批量写入 |
| `query_memories` | 结构化查询 |
| `get_memory` | 按 ID 获取完整内容 |
| `get_memory_stats` | 知识库统计 |
| `verify_memory` | 确认知识仍有效 |
| `update_memory` | 更新知识 |
| `delete_memory` | 删除知识 |
| `condense_module_knowledge` | 压缩单模块知识到 `NodeKnowledge` |
| `condense_all_module_knowledge` | 全量压缩所有模块知识 |
| `get_execution_pipeline_config` | 读取客户端执行管线配置 |
| `update_execution_pipeline_config` | 更新客户端执行管线配置 |
| `run_execution_pipeline` | 执行“架构师->开发者”管线 |
| `get_latest_pipeline_run` | 查看最近一次执行结果 |

## 设计哲学

> **大道至简，万物归一。**
>
> 图中只有一种节点，每个节点自带知识。
> 节点之间只有三种关系：包含（树）、依赖（DAG）、协作（CrossWork）。
> 依赖自由穿越组织边界，唯一禁令是不能成环。
> 环路不是违规，而是重构信号——循环依赖的节点本质上是一个内聚体。

详细设计文档：[docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)

## 2026-03 客户端拆分说明

- `Server` 现在是独立知识服务器：仅提供 `REST API + Dashboard + SQLite`。
- `Client` 现在是独立 MCP/Agent 接入层：对外暴露 `/mcp`，并直连 `Server`。
- 拆分目的：解决多端共享知识库时的并发写入与冲突问题；Git/P4 更适合源码版本管理，不适合作为在线知识写入协调层。

## 2026-03 同步说明

- 存储已切换为 DB-first：图谱与记忆统一使用 SQLite。
- 不再依赖 `architecture.json`、`modules.json`、`memory/entries/*.json`。
- 记忆按 `NodeId` 归属到单节点，短期记忆可提炼为节点长期知识（`NodeKnowledge`）。
- 支持模块知识压缩（单模块 / 全量）及可配置的定时压缩调度（API / Dashboard）。

## 许可证

[Apache 2.0](LICENSE)
