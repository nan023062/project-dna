# Agentic OS

**面向 AI Agent 的本地项目知识运行时。**

Agentic OS 让 Agent 具备项目级认知能力：理解结构、依赖、约束、设计决策与经验沉淀，而不只是修改当前文件。

[English](README.md)

## 当前产品形态

当前文档只保留并描述 **单 App 方案**：

- 单进程
- 单窗口
- 单心智
- 单生命周期

桌面 App 进程内部会嵌入本地运行时，固定监听：

```text
http://127.0.0.1:5052
```

这个本地运行时同时服务三类入口：

- 桌面 UI 自身调用
- 本地 CLI
- Cursor / Codex / 其他 IDE Agent 的 MCP 接入

## 运行拓扑

```text
用户
  |
  +--> Agentic OS App 桌面宿主（单窗口）
         - 项目加载
         - 知识图谱预览
         - 知识预览
         - 工作区状态
         - 本地 agent shell
         - 工具接入 / MCP 入口
         - 进程内嵌本地运行时 :5052
                |
                +--> 桌面 UI
                +--> agentic-os cli
                +--> Cursor / Codex / 其他 IDE Agent 通过 /mcp 接入
```

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
- 只有在桌面 App 成功加载项目后，本地 `5052` 运行时才会真正对外可用

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

- `knowledge/`：知识图谱真相源（模块身份、层级、依赖）
- `memory/`：长期记忆（决策、约定、教训、摘要）
- `session/`：短期工作记忆（任务、上下文）
- `logs/`：App 日志

当前本地知识库与桌面运行时都围绕这个目录初始化；其中工作区配置等用户级状态已迁移到用户目录，不再作为项目真相源的一部分。

## App 本地运行时接口

进程内嵌本地运行时当前暴露：

- `/mcp`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/api/app/status`
- `/api/app/workspaces/*`
- `/api/app/tooling/*`
- `/agent/*`

这些接口只用于支撑桌面宿主、CLI 与 IDE 集成，不代表存在第二套独立 Web 产品。

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

此外，App 也暴露 `GET /api/app/mcp/tools`，供桌面 UI 与自动化读取完整 MCP 工具清单。

## 架构说明

- 当前受支持的运行模式是 **单 App**
- 本地运行时与桌面窗口共处同一进程
- 桌面 UI、CLI、MCP 全部收敛到同一个本地 `:5052` 表面
- 知识图谱正在持续拆分为 scene、layout、render、cache、LOD 等层

详见：

- [src/Dna.Workbench/ARCHITECTURE.md](src/Dna.Workbench/ARCHITECTURE.md)
- [src/Dna.Knowledge/ARCHITECTURE.md](src/Dna.Knowledge/ARCHITECTURE.md)

## 许可证

[Apache 2.0](LICENSE)
