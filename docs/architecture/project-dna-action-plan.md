# Project DNA：行动计划

> **作者**：李南
> **日期**：2026-03-28
> **最后更新**：2026-03-28

---

## 一、当前进度盘点

### 已完成

| 里程碑 | 状态 | 说明 |
|--------|------|------|
| 统一节点模型 `KnowledgeNode` | ✅ | 四种 NodeType，节点自带 `NodeKnowledge` 物化视图 |
| 三引擎拆分 | ✅ | `GraphEngine` + `MemoryEngine` + `GovernanceEngine` |
| MCP 工具面 | ✅ | `KnowledgeTools`（17 个）+ `MemoryTools`（12 个） |
| 记忆四通道检索 | ✅ | 向量 + FTS + 标签 + 坐标，融合排序 + 约束链展开 |
| 治理顾问模式 | ✅ | 环路检测→重构建议、鲜活度衰减、冲突检测 |
| 多角色解释器 | ✅ | Coder / Designer / Art |
| 记忆存储纯 SQLite 化 | ✅ | 去掉 JSON 双写，SQLite 为唯一数据源 |
| REST API 补全 | ✅ | GraphEndpoints + GovernanceEndpoints，覆盖所有 MCP 对应能力 |
| CLI 命令补全 | ✅ | validate / search / recall / stats / export / import |
| Auth 认证系统 | ✅ | JWT + 用户表 + 角色（admin/editor/viewer）+ 自动创建 admin |
| Dashboard | ✅ | 拓扑浏览（只读）+ 记忆管理 + LLM 对话 |
| Server 独立运行 | ✅ | `--db` 指定知识库路径，不依赖项目源码，Banner 显示局域网 IP |

---

## 二、Server 运行模型

### 核心概念

Server 是一个**知识服务**，不直接读写项目源码。客户端（IDE Agent）通过 MCP/API 写入和读取知识。Server 只需要一个目录来存储 SQLite 数据库。

### 启动方式

```bash
cd /data/dna/my-game && dna --db            # 当前目录作为知识库
dna --db /data/dna/my-game                   # 指定知识库路径
dna --db /data/dna/my-game --port 5051       # 指定端口
dna --db --port 5051                         # 当前目录 + 指定端口
```

`--db` 必须指定（可不带路径参数，默认用当前目录）。也可通过环境变量 `DNA_STORE_PATH` 指定。

### 架构

```
┌──────────────────────────────────────────────────┐
│                DNA Server                          │
│                                                    │
│  ┌─ HTTP API ──────────────────────────────┐      │
│  │  /api/graph/*       图谱查询 + CRUD      │      │
│  │  /api/memory/*      记忆 CRUD + 语义检索  │      │
│  │  /api/governance/*  治理报告              │      │
│  │  /api/auth/*        注册/登录/Token       │      │
│  └──────────────────────────────────────────┘      │
│                                                    │
│  ┌─ MCP Server ────────────────────────────┐      │
│  │  IDE 直连（stdio / HTTP transport）      │      │ ← Cursor / Codex
│  └──────────────────────────────────────────┘      │
│                                                    │
│  ┌─ CLI ───────────────────────────────────┐      │
│  │  dna cli status / validate / recall ... │      │ ← 人类 / 脚本 / CI
│  └──────────────────────────────────────────┘      │
│                                                    │
│  ┌─ Dashboard (wwwroot) ───────────────────┐      │
│  │  架构拓扑浏览（只读）                     │      │
│  │  记忆增删查改                             │      │ ← 浏览器
│  │  LLM 配置 + AI 对话                      │      │
│  └──────────────────────────────────────────┘      │
│                                                    │
│  三引擎 (Graph + Memory + Governance)              │
│  存储: SQLite（唯一数据源，存在 --db 指定的目录下）  │
│  认证: JWT + 角色 (admin/editor/viewer)             │
│  Banner: 启动后显示局域网 IP，可直接复制使用         │
│                                                    │
│  Server 不访问项目源码，客户端负责写入知识          │
└──────────────────────────────────────────────────┘
```

### 多项目部署（远程服务器）

```bash
dna --db /data/dna/game-a --port 5051
dna --db /data/dna/game-b --port 5052
dna --db /data/dna/web-app --port 5053
```

启动后 Banner 输出：

```
  REST API:    http://192.168.1.100:5051/api/
  MCP Server:  http://192.168.1.100:5051/mcp
  Dashboard:   http://192.168.1.100:5051
  知识存储:    /data/dna/game-a
```

IDE 端 `.cursor/mcp.json`：

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://192.168.1.100:5051/mcp"
    }
  }
}
```

### Dashboard 功能范围

| 功能 | 说明 |
|------|------|
| 架构拓扑图浏览 | 只读，可视化节点、依赖、CrossWork |
| 记忆增删查改 | 筛选、搜索、编辑、删除 |
| 治理统计 | 鲜活度检查、冲突检测、归档 |
| LLM 配置 + AI 对话 | 配置模型，与知识库交互 |

不在 Dashboard 做的：项目路径配置（Server 不需要）、架构配置/模块注册（通过 MCP/CLI/API）。

---

## 三、下一步方向

当前阶段（M1）已完成。详见 [roadmap.md](./project-dna-roadmap.md)。

| 方向 | 对应 Milestone | 简述 |
|------|---------------|------|
| 核心模型深化 | M2 | Edge 一等公民、Knowledge 物化视图、增量拓扑 |
| 生态基础 | M3 | Template + Scanner + `dna init` / `dna scan` CLI |
| 公开发布 | M4 | HN/Reddit/掘金 + 演示视频 + 证明通用性 |
| 生态扩展 | M5 | 治理规则包 + Extractor + Interpreter 扩展 |
| 企业级 | M6 | PostgreSQL 后端 + RBAC + 多租户 + Agent Runtime |

---

## 四、原则

> **`--db` 是启动的唯一入口**——指向知识库目录，Server 不需要项目源码。
>
> **HTTP API 是基座**——MCP、CLI、Dashboard 都是它的消费层。
>
> **Dashboard 只读架构、只管记忆**——架构变更通过 MCP/CLI/API。
>
> **不做独立 Client 项目**——IDE 直连 Server MCP，Dashboard 是 Server 前端。
>
> **发布节奏不变：能跑就发，完成比完美重要 100 倍。**
