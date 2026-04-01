# Project DNA

**面向 AI Agent 的本地项目知识运行时。**

Project DNA 让 Agent 具备项目级认知能力：理解结构、依赖、约束、设计决策与经验沉淀，而不只是修改当前文件。

[English](README.md)

## 当前产品形态

当前文档只保留并描述 **单 Client 方案**：

- 单进程
- 单窗口
- 单心智
- 单生命周期

桌面 Client 进程内部会嵌入本地运行时，固定监听：

```text
http://127.0.0.1:5052
```

这个本地运行时同时服务三类入口：

- 桌面 UI 自身调用
- 本地 CLI
- Cursor / Codex / 其他 IDE Agent 的 MCP 接入

仓库里可能仍暂时存在旧 `Server` 代码，但它**不是当前文档描述的主架构**，后续会被清理。

## 运行拓扑

```text
用户
  |
  +--> Agentic OS Client 桌面宿主（单窗口）
         - 项目加载
         - 知识图谱预览
         - 知识预览
         - 工作区状态
         - 本地 agent shell
         - 工具接入 / MCP 入口
         - 进程内嵌本地运行时 :5052
                |
                +--> 桌面 UI
                +--> dna_client cli
                +--> Cursor / Codex / 其他 IDE Agent 通过 /mcp 接入
```

## 快速开始

### 1. 构建

```bash
dotnet build src/Client/Client.csproj
```

### 2. 启动桌面 Client

开发模式：

```bash
dotnet run --no-launch-profile --project src/Client
```

发布产物：

```bash
publish/client/dna_client.exe
```

### 3. 准备项目目录

桌面 Client 需要加载一个包含下列文件的项目目录：

```text
.project.dna/project.json
```

最小示例：

```json
{
  "projectName": "agentic-os"
}
```

说明：

- 历史文件里如果仍有 `serverBaseUrl` 字段，可以保留，但当前单 Client 运行时并不依赖它
- 只有在桌面 Client 成功加载项目后，本地 `5052` 运行时才会真正对外可用

### 4. 接入 Cursor / Codex

桌面 Client 加载项目后，把 IDE 的 MCP 配置指向：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

## 项目级状态目录

当前 Client 把项目级状态存放在 `.project.dna/` 下：

- `project.json`：项目身份信息
- `llm.json`：Client 运行时大模型配置预留
- `logs/`：Client 日志
- `client-workspaces.json`：工作区状态
- `agent-shell/agent-shell-state.json`：本地 agent shell 状态

当前本地知识库也基于这个项目级元数据根目录初始化。

## Client 本地运行时接口

进程内嵌本地运行时当前暴露：

- `/mcp`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/api/client/status`
- `/api/client/workspaces/*`
- `/api/client/tooling/*`
- `/agent/*`

这些接口只用于支撑桌面宿主、CLI 与 IDE 集成，不代表存在第二套独立 Web 产品。

## CLI

当前桌面 Client 自带本地 CLI 入口：

```bash
dna_client cli status
dna_client cli topology
dna_client cli search render
dna_client cli recall "有哪些约束"
dna_client cli memories
dna_client cli tools
```

默认本地运行时地址：

```text
http://127.0.0.1:5052
```

## MCP 工具

### 图谱工具

| 工具 | 说明 |
|------|------|
| `get_topology` | 查看完整知识图谱 |
| `get_context` | 获取模块上下文、约束、依赖与经验 |
| `search_modules` | 按关键词搜索节点 |
| `get_dependency_order` | 获取多模块修改的依赖顺序 |
| `register_module` | 新增或修改知识节点 |
| `register_crosswork` | 声明跨模块协作关系 |
| `validate_architecture` | 进行架构健康检查 |

### 记忆工具

| 工具 | 说明 |
|------|------|
| `remember` | 写入知识 |
| `recall` | 语义检索记忆 |
| `batch_remember` | 批量写入 |
| `query_memories` | 结构化查询 |
| `get_memory` | 按 ID 读取记忆 |
| `get_memory_stats` | 查看知识库统计 |
| `verify_memory` | 确认记忆是否仍有效 |
| `update_memory` | 更新记忆 |
| `delete_memory` | 删除记忆 |
| `condense_module_knowledge` | 将单模块知识压缩为长期知识 |
| `condense_all_module_knowledge` | 执行全量知识压缩 |

此外，Client 也暴露 `GET /api/client/mcp/tools`，供桌面 UI 与自动化读取完整 MCP 工具清单。

## 架构说明

- 当前受支持的运行模式是 **单 Client**
- 本地运行时与桌面窗口共处同一进程
- 桌面 UI、CLI、MCP 全部收敛到同一个本地 `:5052` 表面
- 知识图谱正在持续拆分为 scene、layout、render、cache、LOD 等层
- 仓库中的旧 Server 代码只视为过渡残留，不再作为当前产品文档的一部分

详见：

- [docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)
- [docs/architecture/project-dna-transport-auth-decision.md](docs/architecture/project-dna-transport-auth-decision.md)
- [docs/architecture/client-topology-upgrade-plan.md](docs/architecture/client-topology-upgrade-plan.md)

## 许可证

[Apache 2.0](LICENSE)
