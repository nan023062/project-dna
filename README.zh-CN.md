# Project DNA

**项目的认知引擎。**

Mem0 让 Agent 记住对话，GitNexus 让 Agent 看懂代码语法，CLAUDE.md 存个人备忘。
**Project DNA 让 AI Agent 理解整个项目**——结构、知识、依赖、约束和跨团队协作——让 Agent 带着完整上下文精准修改文件。

[English](README.md)

---

## 解决什么问题

AI Agent 写代码越来越强，但仍缺乏**项目级认知**：

- 不知道当前在哪个模块、有什么约束
- 看不到架构级的跨文件依赖
- 不记得团队踩过的坑和约定的规范
- 完全没有跨工种协作的概念（程序 + 美术 + 策划）

**结果**：Agent 写出能编译但违反架构、破坏隐式契约、忽略教训的代码。

## 怎么解决

Project DNA 从项目文件系统中提取**结构、知识和关系**，编码为可查询的知识图谱。Agent 修改文件前先读 DNA。

```
文件系统（几千个文件）
      ↓ 提炼
工程 DNA（几十个带知识的节点）
      ↓ 通过 MCP 服务
AI Agent（精准的、上下文感知的文件修改）
```

## 核心概念

### 一种节点，所有组织形态

所有实体——模块、小组、部门、工作室、跨职能专班——都是同一个 `KnowledgeNode`：

| NodeType | 软件类比 | 组织类比 |
|----------|---------|---------|
| `Root` | System | 工作室/项目 |
| `Department` | Package | 部门/大组 |
| `Module` | Class/Service | 小组/工种 |
| `CrossWork` | Composite | 工作专班 |

### 三种正交关系

| 关系 | 建模什么 | 规则 |
|------|---------|------|
| **包含**（树） | 组织归属 | 每个节点一个父节点 |
| **依赖**（DAG） | 「我用你的输出」 | 不能成环；自由跨越组织边界 |
| **协作**（CrossWork） | 「我们一起交付」 | 多方共建，不是依赖的替代品 |

### 节点自带知识

每个节点有物化的 `Knowledge` 视图——身份、约束、教训、当前任务。一次 MCP 调用返回完整上下文。

### 环路 = 合并信号

循环依赖不是违规，而是**重构建议**。A 依赖 B、B 依赖 A → 它们应该合并或组建小组。

## 快速开始

### 1. 编译

```bash
cd src
dotnet build
```

### 2. 运行

```bash
# 进入知识库目录直接启动
cd /data/dna/my-game && dna --db

# 指定知识库路径
dna --db /data/dna/my-game

# 指定端口
dna --db /data/dna/my-game --port 5052

# 当前目录 + 指定端口
dna --db --port 5051

# stdio 模式（由 IDE 启动）
dna --stdio --db /data/dna/my-game
```

`--db` 必须指定，指向知识库存储目录。Server 不访问项目源码，客户端通过 MCP/API 写入知识。

### 3. 接入 Cursor

在项目根目录创建 `.cursor/mcp.json`：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5051/mcp"
    }
  }
}
```

远程服务器使用对应 IP 和端口：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://192.168.1.100:5051/mcp"
    }
  }
}
```

### 4. Dashboard

浏览器打开 `http://localhost:5051`，可以：
- 浏览架构拓扑图（只读）
- 增删查改记忆
- 配置 LLM + AI 对话

### 5. CLI

```bash
dna cli status                          # 服务状态
dna cli validate                        # 架构健康检查
dna cli search combat                   # 搜索模块
dna cli recall "有什么约束"              # 语义检索记忆
dna cli stats                           # 知识库统计
```

## 设计哲学

> **大道至简，万物归一。**
>
> 图中只有一种节点，每个节点自带知识。
> 节点之间只有三种关系：包含（树）、依赖（DAG）、协作（CrossWork）。
> 依赖自由穿越组织边界，唯一禁令是不能成环。
> 环路不是违规，而是重构信号——循环依赖的节点本质上是一个内聚体。
>
> 如果两个人总是需要坐在一起才能干活，那他们就该在同一个工位。
> 如果两个组经常需要协作，就成立一个专班。
> 如果你发现自己在画环形依赖图，说明组织边界画错了。

详细设计文档：[docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)

## 许可证

[Apache 2.0](LICENSE)
