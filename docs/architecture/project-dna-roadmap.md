# Project DNA：Roadmap

> **作者**：李南
> **日期**：2026-03-28
> **关联文档**：
> - [核心设计](./project-dna-design.md) — 统一节点模型、三引擎、图的三种关系
> - [生态扩展](./project-dna-ecosystem.md) — 模板、扫描器、治理规则包、知识提取器
> - [价值定位](./project-dna-value-proposition.md) — 竞品分析、差异化、风险应对
> - [当前阶段行动计划](./project-dna-action-plan.md) — Server 多人协作版

---

## 全景时间线

```
 M0 ✅          M1 ✅           M2             M3             M4            M5             M6
 核心引擎      Server多人版     模型深化        生态基础        公开发布       生态扩展        企业级
 (已完成)      (已完成)                                      + 社区引爆
  │             │               │              │              │             │              │
──┼─────────────┼───────────────┼──────────────┼──────────────┼─────────────┼──────────────┼──→
  │             │               │   ~2 周       │   ~2 周       │   ~1 周      │   ~3 周      │
  │             │               │              │              │             │
  单体          REST+CLI+Auth   Edge一等公民    Template       HN/Reddit     Rules/Extract
  本地版        Dashboard       增量拓扑        Scanner        博客/视频      Interpreter
                纯SQLite存储    知识物化        dna init       证明通用性     Storage
```

---

## M0：核心引擎 ✅ 已完成

> 对应 design.md Phase 1

| 交付物 | 状态 |
|--------|------|
| 统一节点模型 `KnowledgeNode`（4 种 NodeType） | ✅ |
| 三引擎拆分：`GraphEngine` / `MemoryEngine` / `GovernanceEngine` | ✅ |
| MCP 工具面：29 个工具（KnowledgeTools 17 + MemoryTools 12） | ✅ |
| 记忆四通道检索（向量 + FTS + 标签 + 坐标） | ✅ |
| 治理顾问模式（环路→重构建议、鲜活度衰减、冲突检测） | ✅ |
| 多角色解释器（Coder / Designer / Art） | ✅ |
| REST API 6 组 + 英文/中文 README | ✅ |

**当前状态**：可用的单人本地版本。Cursor IDE 直连单体 Server，无认证，存储绑定本地目录。

---

## M1：Server 多人协作版 ✅ 已完成

> 详细记录见 [action-plan.md](./project-dna-action-plan.md)

**目标**：专注 Server，做成完整的、可多人使用的独立服务。

| 交付物 | 状态 |
|--------|------|
| 记忆存储纯 SQLite 化（去掉 JSON 双写） | ✅ |
| REST API 补全（GraphEndpoints + GovernanceEndpoints） | ✅ |
| CLI 命令补全（validate / search / recall / stats / export / import） | ✅ |
| Auth 认证（JWT + UserStore + 角色 admin/editor/viewer） | ✅ |
| Dashboard 精简（拓扑浏览 + 记忆管理 + LLM 对话，去掉项目选择和架构配置） | ✅ |
| 启动强制 `--project` 参数（一个 Server = 一个项目） | ✅ |

**运行模型**：

```
dna --project /path/to/project --port 5051

IDE ──MCP──→ Server ──→ SQLite
CLI ──HTTP──→    ↑
Dashboard ──→    │
```

---

## M2：核心模型深化

> 对应 design.md Phase 2 + Phase 4

**目标**：完善图模型的核心能力，为生态层做准备。

### M2.1 Edge 一等公民

| 任务 | 说明 |
|------|------|
| Edge 独立 CRUD | 不再通过修改 `Dependencies` 列表间接实现，Edge 有自己的 API |
| `KnowledgeEdge` 持久化 | 独立存储 + 独立查询（按 From/To 过滤） |
| HTTP API + MCP 工具 | `/api/graph/edges` + `add_edge` / `remove_edge` / `query_edges` |

### M2.2 节点 Knowledge 物化视图

| 任务 | 说明 |
|------|------|
| 记忆写入后自动刷新节点 Knowledge | `MemoryEngine.Remember` → 回调 `GraphEngine.RefreshKnowledge(nodeId)` |
| `begin_task` 简化 | 直接读节点 Knowledge，一次调用返回 80% 上下文 |
| Knowledge 摘要自动生成 | 从归属记忆中提炼 Identity / Lessons / Facts / ActiveTasks |

### M2.3 增量拓扑 + 性能优化

| 任务 | 说明 |
|------|------|
| 增量拓扑更新 | 节点/边变更只重算受影响的子图，不全量重建 |
| 脏检查 + 延迟重算 | 批量操作期间标记脏，操作结束后一次性重算 |
| 批量操作优化 | `batch_add_nodes` / `batch_add_edges` |

**里程碑验收**：
- Edge 有独立的增删查 API
- 写入记忆后，对应节点的 Knowledge 自动更新
- 100+ 节点的项目拓扑构建 < 100ms

---

## M3：生态基础 — Template + Scanner

> 对应 ecosystem.md Phase 1（P0 优先级）

**目标**：让新用户 5 分钟上手，不再需要手动逐个注册节点。

### M3.1 Template 机制

| 任务 | 说明 |
|------|------|
| `IDnaTemplate` 接口 | 定义模板的结构：节点树 + 依赖 + 默认知识 + 推荐 Scanner |
| `dna init --template=<name>` | CLI 命令，从模板初始化 DNA |
| 模板继承 | `unity-game` 继承 `generic-game`，避免重复 |
| 3 个首发模板 | `unity-game` / `react-app` / `generic` |

**`dna-template.json` 格式**：

```json
{
  "name": "unity-game",
  "version": "1.0.0",
  "nodes": [
    { "name": "Root", "type": "Root", "knowledge": { "identity": "Unity 游戏项目" } },
    { "name": "技术部", "type": "Department", "parent": "Root" },
    { "name": "引擎框架", "type": "Module", "parent": "技术部", "pathPattern": "Assets/Scripts/Framework/**" }
  ],
  "suggestedScanners": ["csharp", "unity-asmdef"],
  "suggestedRules": ["unity-performance-rules"]
}
```

### M3.2 Scanner 机制

| 任务 | 说明 |
|------|------|
| `IDnaScanner` 接口 | `FilePatterns` / `CanScan` / `Scan` → `ScanResult`（节点 + 边 + 知识） |
| `dna scan` CLI 命令 | 自动检测项目类型，运行匹配的 Scanner，输出节点/依赖/知识 |
| `--dry-run` 模式 | 预览扫描结果，不写入 |
| 3 个首发 Scanner | `CSharpScanner`（.csproj + using）/ `TypeScriptScanner`（import + package.json）/ `DirectoryScanner`（通用目录结构推导） |

### M3.3 CLI 工具

| 命令 | 说明 |
|------|------|
| `dna init` | 选模板初始化 |
| `dna scan` | 自动扫描生成 DNA |
| `dna serve` | 启动 Server |
| `dna connect` | 启动 Client，连接远程 Server |
| `dna validate` | 运行治理检查 |
| `dna status` | 查看当前 DNA 状态 |

**里程碑验收**：
- `dna init --template=unity-game && dna scan` 一行命令生成完整 DNA
- 用户从零到 Agent 可用 < 5 分钟
- 这是消除竞品分析中 **「风险 1：太重了」** 的关键一步

---

## M4：首次公开发布 + 社区引爆

> 对应 value-proposition.md 的差异化定位

**目标**：让世界知道 Project DNA 的存在，验证市场需求。

### M4.1 发布准备

| 任务 | 说明 |
|------|------|
| 录制 2 分钟演示视频 | `dna init` → `dna scan` → Cursor IDE 使用 → Dashboard 可视化 |
| 英文博客 ×1 | 设计哲学：「One Node Type, Three Relationships」 |
| 证明通用性 | 用 `react-app` 模板做一个非游戏项目的完整演示 |
| CONTRIBUTING.md 完善 | 模板/扫描器/规则包的贡献指南 |
| Apache 2.0 LICENSE | 已有 |

### M4.2 社区引爆（同一天发布）

| 渠道 | 形式 |
|------|------|
| Hacker News | Show HN: Project DNA — The cognitive engine for your project |
| Reddit | r/programming + r/MachineLearning + r/gamedev |
| Twitter/X | 线程 + 演示 GIF |
| V2EX | 创意分享 |
| 掘金 | 中文技术文章 |

### M4.3 发布后指标（第 8 周末看数据）

| 信号 | 判断 |
|------|------|
| GitHub Star > 500 | 有关注度 |
| GitHub Star > 2000 | 市场认可 |
| 外部用户在真实项目使用 | 有真实需求 |
| 有人提 PR（模板/Scanner） | 社区飞轮启动 |
| 无上述信号 | 调整方向，继续迭代 |

**核心叙事**（来自 value-proposition.md）：

> Mem0 让 Agent 记住用户，GitNexus 让 Agent 看懂代码，
> **Project DNA 让 Agent 理解整个项目。**
>
> 这个位置，现在没人占。

---

## M5：生态扩展

> 对应 ecosystem.md Phase 2-3

前提：M4 的市场信号正向（Star 持续增长、有外部用户、有社区贡献）。

### M5.1 治理规则包（Governance Rules）

| 任务 | 说明 |
|------|------|
| `IGovernanceRule` 插件接口 | `Check(node, topology)` → `GovernanceSuggestion?` |
| 可插拔规则加载 | `dna install rules <pack-name>` |
| 2 个首发规则包 | `clean-architecture-rules` / `unity-performance-rules` |

### M5.2 知识提取器（Extractors）

| 任务 | 说明 |
|------|------|
| `IKnowledgeExtractor` 接口 | 从已有文档/系统提取知识灌入节点 |
| 3 个首发提取器 | `MarkdownExtractor` / `GitHistoryExtractor` / `CodeCommentExtractor`（`// DNA:` 标注） |

### M5.3 角色解释器扩展（Interpreters）

| 角色 | 视角 |
|------|------|
| `qa` | 测试用例、回归清单、已知缺陷 |
| `devops` | 部署依赖、环境要求、监控指标 |
| `pm` | 进度、里程碑、资源瓶颈、风险 |
| `ta` | 着色器规范、美术管线约束 |

### M5.4 社区基础设施

| 任务 | 说明 |
|------|------|
| `dna-registry` 仓库 | 社区扩展的集散地（模板 / Scanner / Rules / Extractor） |
| CI 验证 | PR 提交自动测试模板合法性、Scanner 输出格式 |
| `dna install <type> <name>` | 统一安装命令 |

**社区飞轮**：

```
更多模板/扫描器 → 更多项目类型的用户 → 更多贡献者 → 更多模板/扫描器 …
```

**里程碑验收**：
- `dna install rules clean-architecture-rules && dna validate` 可用
- 社区有人贡献第三方 Scanner 或模板
- `dna-registry` 仓库有 10+ 扩展

---

## M6：企业级

> 对应 value-proposition.md 的 Open Core 模式 + ecosystem.md 的 Storage/Interpreter 扩展

前提：M5 的社区飞轮已启动，有明确的企业客户需求信号。

### M6.1 存储后端扩展

| Backend | 场景 |
|---------|------|
| `SqliteBackend`（默认） | 单机 / 小团队 — 零配置 |
| `PostgresBackend` | 大团队 / 企业 — 并发、备份、审计 |
| `GitNativeBackend` | 离线 / 纯文件偏好 — 类 Pensieve 降级模式 |

### M6.2 企业功能（Open Core 收费层）

| 功能 | 说明 |
|------|------|
| 高级 RBAC | 细粒度权限：按部门/节点/操作类型控制 |
| 多租户 | 一个 Server 服务多个项目/团队 |
| 审计日志 | 谁在什么时间改了什么知识 |
| SSO 集成 | LDAP / OAuth / SAML |
| Confluence/Jira 集成 | 知识双向同步 |
| 托管服务 | DNA Cloud — 无需自建 Server |

### M6.3 任务编排 + Agent Runtime

| 任务 | 说明 |
|------|------|
| `IOrchestrator` 实现 | 多步任务拆解、依赖排序、并行调度 |
| `IAgentRuntime` 实现 | Agent 自主执行：从 DNA 获取上下文 → 执行 → 写回知识 |
| 与 Mem0/Zep 集成 | DNA 管项目知识，Mem0/Zep 管对话记忆，互不冲突 |

---

## 风险应对矩阵

> 来源：value-proposition.md 第五节

| 风险 | 在哪个阶段解决 | 应对措施 |
|------|---------------|----------|
| **「太重了，不如用 CLAUDE.md」** | M3（Template + Scanner） | `dna init` 5 分钟上手，Scanner 自动生成，门槛降到和 `create-react-app` 一样低 |
| **「代码分析不如 GitNexus」** | M5（Extractor 集成） | DNA 关注组织认知不竞争代码语法；Scanner 插件可集成第三方 AST 分析结果 |
| **「Mem0/Zep 已经很成熟」** | M6（集成） | DNA 的差异化在「图」不在「记忆」；长期与 Mem0/Zep 互补集成 |
| **「只适合游戏团队」** | M3 + M4（多模板 + 非游戏演示） | 首批模板覆盖 Unity + React + Generic；公开发布时用 React 项目证明通用性 |

---

## 关键决策点

| 时间点 | 看什么 | 决定什么 |
|--------|--------|----------|
| M1 完成后 | C/S 全链路是否稳定 | 是否继续投入 M2 还是先发布 |
| M4 发布后第 8 周 | GitHub Star、外部用户、PR | 是否加速迭代还是调整方向 |
| M5 社区飞轮 | 第三方贡献数量 | 是否投入 dna-registry 基础设施 |
| M6 企业信号 | 有企业客户付费意愿 | 是否启动 Open Core 收费层、是否全职 |

---

## 总时间估算

| 阶段 | 预估时间 | 累计 | 前提 |
|------|---------|------|------|
| M0 核心引擎 | — | — | ✅ 已完成 |
| M1 Server 多人版 | — | — | ✅ 已完成 |
| M2 模型深化 | 2 周 | 2 周 | — |
| M3 生态基础 | 2 周 | 1 个月 | — |
| M4 公开发布 | 1 周准备 | 1 个月 | M2-M3 完成 |
| M5 生态扩展 | 3 周 | 2 个月 | M4 信号正向 |
| M6 企业级 | 持续 | 3-6 个月+ | M5 飞轮启动 |

**全程不辞职，直到 M4 的市场信号给出明确答案。**

---

## 一句话总结

> **M1 让它能用，M3 让它好用，M4 让世界知道，M6 让它赚钱。**
>
> 每个里程碑都是可独立交付的产品状态。
> 不存在「做到一半不能用」的中间态。
> 完成比完美重要 100 倍——能跑就发。
