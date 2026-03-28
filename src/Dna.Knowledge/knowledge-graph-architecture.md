# 知识图谱架构设计

> Project DNA 的项目治理模型：以组织架构比喻驱动模块管理。

## 零、为什么这个模型是通用的

整个架构只有三条规则：**分层单向、同层隔离、协作走契约**。这三条规则不绑定任何具体领域，因此可以映射到几乎所有层级组织：

| 场景 | L0（基础/规范） | L1（支撑/领域） | L2（业务/交付） | CrossWork 示例 |
|------|----------------|----------------|----------------|---------------|
| **行政治理** | 宪法/国策 | 省级政策 | 市县执行 | "长三角一体化" |
| **团队管理** | 架构规范 | 引擎/工具/运维 | 玩法/系统/内容 | "热更新系统" |
| **编程架构** | Foundation | Services | Domain / Presentation | "用户注册流程" |
| **游戏项目** | Core/Math | Physics/Rendering | Combat/Building | "火球术技能" |

模块不一定是代码——它可以是一组美术资产、一份策划配置、一套规范文档。规则统一，产出物不限。

## 一、四大核心概念

知识图谱由四个正交的核心概念组成，各自职责清晰、互不侵入：

```
架构（architecture.json）
  定义组织结构和规则 — "宪法"
  ├── 部门（Discipline）
  ├── 每个部门的层级（Layers）
  └── 扫描配置（ScanConfig）

模块（modules.json）
  最小工作单元 — "内聚的团队"
  ├── 归属哪个部门、哪个层级
  ├── 声明依赖（架构意图）
  └── 只能向下依赖，同层隔离

CrossWork 工作组模块（modules.json 中 IsCrossWorkModule=true 的模块）
  跨模块协议/方案的承载体 — "项目组"
  ├── 本身也是一个模块（有路径、有目录）
  ├── 归属和层级由成员自动推算（不可手动修改）：
  │   ├── 所有成员同部门 → 归属该部门，Layer = 成员最大 Layer
  │   └── 成员跨部门 → 归属 root（项目级），无 Layer
  ├── 可访问任意非 CrossWork 模块（免依赖校验）
  ├── 不被任何其他模块依赖（违规）
  ├── CrossWork 模块之间互相隔离（不可互访）
  ├── 不产生拓扑依赖边，不影响分层计算
  ├── Participants 描述工作组成员及其职责/契约
  └── 目录内承载协议文档、方案、交付物定义

记忆（MemoryEntry + SQLite）
  非结构化知识 — "经验和智慧"
  ├── #identity — 模块身份（JSON）
  ├── #lesson — 踩坑教训（JSON）
  ├── #active-task — 当前任务（JSON）
  └── 自由标签 — 任意知识（语义检索、约束链）
```

### 组织比喻

| 组织概念 | Project DNA 对应 | 说明 |
|----------|-----------------|------|
| 公司 | 项目（TopologySnapshot） | 包含所有部门、模块、协作小组 |
| 部门 | Discipline（engineering / art / design / qa …） | 按职能划分，各自独立管理 |
| 部门内层级 | Layers（预定义层级，如 L0/L1/L2） | 模块注册时必须从本部门 Layers 中选择 |
| 团队/组 | GraphNode（模块） | 最小工作单元 |
| 跨部门项目组 | CrossWork 工作组模块 | 跨模块协议/方案的承载体，拥有特殊访问特权 |

## 二、三条铁律

### 1. 分层单向依赖

- 高层模块可以依赖低层模块（向下依赖）
- **低层不得依赖高层**（反向依赖 = 违规）
- 每个 Discipline（部门）**独立定义**自己的层级
- 模块注册时主动声明层级（`Layer`），系统从依赖关系推导计算层级（`ComputedLayer`），两者偏差由治理报告检出

### 2. 同层零直接依赖

- 同一个 Discipline 内、同一 Layer 的模块**不得有 Dependencies**
- 同层模块的业务耦合必须通过 CrossWork 声明
- 不同 Discipline 之间也**不允许直接依赖**，统一走 CrossWork

### 3. 内部自治 + 外部协作

- 每个模块有自己的 Contract（对外接口声明）
- 模块内部实现自主管理（内聚封装）
- 边界由层级关系动态决定：对上层开放，对同层/下层封闭
- 对外交互只有两种合法方式：
  - **Dependency**：跨层引用（高 → 低），通过 Contract 访问
  - **CrossWork 工作组模块**：跨模块协作方案，通过声明式契约对齐

### 4. CrossWork 工作组模块的访问规则

CrossWork 工作组模块是一种特殊模块（`IsCrossWorkModule=true`），遵循以下规则：

**访问规则：**

| 方向 | 规则 | 说明 |
|------|------|------|
| CW → 普通模块 | 自由访问（Current 级别） | 免依赖校验，可读取任意普通模块的全部上下文 |
| 普通模块 → CW | 禁止（Unlinked） | CW 是协调者，不对外提供服务 |
| CW → CW | 禁止（Unlinked） | 工作组之间互相隔离 |

```
普通模块 ←依赖→ 普通模块       ✓ 正常依赖，受层级约束
CrossWork → 普通模块            ✓ 自由访问，免校验
普通模块 → CrossWork            ✗ 违规
CrossWork → CrossWork           ✗ 违规
```

**归属与层级规则（自动推算，不可手动修改）：**

| 成员分布 | 归属 (discipline) | 层级 (layer) |
|----------|-------------------|-------------|
| 所有成员属于同一部门 | 该部门 | 成员中最大 Layer |
| 成员跨多个部门 | `root`（项目级） | 无（layer=0） |
| 无成员 | `root` | 无（layer=0） |

后端保存时自主推算，前端仅做只读展示，不可编辑。

## 三、存储架构

### 3.1 三文件分离

| 文件 | 内容 | 变更频率 | 谁写 |
|------|------|---------|------|
| `architecture.json` | 部门定义 + 层级 + 扫描配置 | 极低（项目初始化时定义） | 人工 / Dashboard |
| `modules.json` | 模块注册 + CrossWork + Features | 中高（AI/人频繁操作） | AI / Dashboard / MCP |
| `modules.computed.json` | 计算依赖（扫描事实） | 中（扫描器写入） | 系统 |

### 3.2 记忆系统（MemoryEntry）

| 数据 | 存储形式 | 标签 | 绑定方式 |
|------|---------|------|---------|
| 模块身份 | Structural 记忆（JSON） | `#identity` | 绑定到 `ModuleId` |
| 教训踩坑 | Episodic 记忆（JSON） | `#lesson` | 绑定到 `ModuleId` |
| 当前任务 | Working 记忆（JSON） | `#active-task` | 绑定到 `ModuleId` |
| 规范/规则 | Semantic 记忆 | `#rule` | 绑定到 `PathPatterns` |

系统保留标签的 Content 必须是 JSON，写入时强制校验 schema。

### 3.3 语义检索与 Embedding

记忆系统提供四通道召回（向量 + FTS + 标签 + 坐标），其中**向量通道**依赖 Embedding（嵌入向量）。

**什么是 Embedding**：把一段文本压缩成一组浮点数（向量），含义相近的文本在向量空间里距离更近。这使得"换种说法问同一件事"也能命中——FTS 做不到这一点。

**为什么需要单独的模型**：Embedding 模型和聊天模型（GPT-4o、DeepSeek-Chat 等）是完全不同的模型——架构不同、训练目标不同、API 端点不同（`/embeddings` vs `/chat/completions`）。聊天模型不能输出向量，Embedding 模型不能对话。因此 LLM Provider 配置中需要**分别指定**聊天模型和 Embedding 模型。

**双模式检索设计**：

| 模式 | 触发条件 | 检索能力 |
|------|---------|---------|
| **向量 + FTS** | Provider 配置了 `EmbeddingModel` | 语义匹配（换种说法也能命中）+ 关键词匹配 |
| **纯 FTS 降级** | 未配置 `EmbeddingModel` | 仅关键词匹配，召回质量较弱但零外部依赖 |

**兼容性原则**：

- **未配置 EmbeddingModel 时不发任何 HTTP 请求**，静默降级到 FTS，不假设任何默认模型
- 不发送 `dimensions` 参数，完全信任模型返回的原生维度，兼容任意 Embedding 提供商
- `VectorIndex` 使用 `Math.Min(a.Length, b.Length)` 做点积，天然兼容不同维度的向量
- 向量以 BLOB 存储在 SQLite 中，维度无固定约束

**配置路径**（Dashboard → LLM 设置）：

| 字段 | 说明 |
|------|------|
| `EmbeddingBaseUrl` | Embedding 端点 Base URL，留空则复用聊天的 `BaseUrl` |
| `EmbeddingModel` | Embedding 模型名（如 `text-embedding-3-small`），**留空则不启用向量检索** |

**常见 Embedding 模型参考**：

| 模型 | 提供商 | 维度 |
|------|--------|------|
| `text-embedding-3-small` | OpenAI / 兼容代理 | 1536 |
| `text-embedding-3-large` | OpenAI / 兼容代理 | 3072 |
| `text-embedding-ada-002` | OpenAI / 兼容代理 | 1536 |

## 四、代码架构（Dna.Knowledge）

### 4.1 模块划分

```
Dna.Knowledge/
├── Models/                          ← 公共共享模型（被所有子模块引用）
│   └── ModuleModels.cs             ← ModulesManifest, ModuleRegistration,
│                                      LayerDefinition, CrossWorkRegistration,
│                                      FeatureDefinition, ComputedManifest
│
├── Project/                         ← 工程目录管理
│   ├── Models/
│   │   ├── ArchitectureManifest.cs  ← ArchitectureManifest, DisciplineDefinition, DefaultExcludes
│   │   └── ProjectFileNode.cs       ← ProjectFileNode, FileNodeStatus, FileNodeActions
│   ├── ProjectScanner.cs           ← 目录扫描 + 状态标记 + 操作推断
│   └── ProjectTreeCache.cs         ← 文件树缓存 + FileSystemWatcher 增量更新
│
├── Graph/                           ← 拓扑图谱
│   ├── Models/                      ← GraphNode, GraphEdge, Department, CrossWork,
│   │                                   TopologySnapshot, GovernanceReport, ContextLevel
│   ├── Internal/
│   │   ├── TopologyBuilder.cs       ← 从三个 manifest 构建拓扑
│   │   ├── GovernanceAnalyzer.cs    ← 架构治理分析
│   │   └── ContextFilter.cs         ← 视界过滤
│   └── Contracts/
│       └── IProjectAdapter.cs       ← 领域适配器接口
│
├── Memory/                          ← 记忆系统
│   ├── Models/                      ← MemoryEntry, MemoryRequests, SystemTagPayloads
│   ├── Store/                       ← MemoryStore（SQLite + JSON 双引擎）
│   ├── Services/                    ← Writer, Reader, RecallEngine, EmbeddingService, VectorIndex
│   └── Governance/                  ← FreshnessChecker, MemoryMaintainer
│
└── KnowledgeGraph.cs                ← 统一入口（Facade）
```

### 4.2 依赖方向（单向，无循环）

```
Models/（公共共享）
  ↑
  ├── Project/Models（引用 Models/ 的 LayerDefinition）
  ├── Project/ProjectScanner（引用 Models/ + Project/Models）
  ├── Graph/*（引用 Models/ + Project/Models + Memory/）
  ├── Memory/*（引用 Models/）
  └── KnowledgeGraph（引用所有子模块，对外唯一入口）
```

## 五、数据模型

### 5.1 ArchitectureManifest（architecture.json）

```
ArchitectureManifest
├── Disciplines{}          ← 部门定义（key = discipline id）
│   └── DisciplineDefinition
│       ├── DisplayName    ← 显示名称（如 "工程部"）
│       ├── RoleId         ← 工种 ID（如 coder/designer/art）
│       └── Layers[]       ← 预定义层级（level + name）
└── ExcludeDirs?           ← 额外排除的目录名（追加到默认排除列表）
```

### 5.2 ModulesManifest（modules.json）

```
ModulesManifest
├── Disciplines{}          ← 按部门组织的模块列表（key = discipline id 或 "root"）
│   └── List<ModuleRegistration>
│       ├── Id             ← 全局唯一标识（ULID）
│       ├── Name           ← 模块名称
│       ├── Path           ← 相对路径
│       ├── Layer          ← 声明层级（CW 模块自动推算）
│       ├── IsCrossWorkModule ← 是否为工作组模块
│       ├── Participants[] ← 工作组成员（仅 IsCrossWorkModule=true）
│       │   ├── ModuleName, Role
│       │   ├── ContractType, Contract
│       │   └── Deliverable
│       ├── Dependencies[] ← 声明依赖（工作组模块忽略此字段）
│       └── Maintainer     ← 维护者
├── CrossWorks[]           ← 遗留兼容：独立 CrossWork 声明（逐步迁移到工作组模块）
└── Features{}             ← 业务系统定义

注：CW 工作组模块的 discipline 和 layer 由后端自动推算：
  - 所有成员同部门 → 存入该部门的模块列表，layer = 成员最大值
  - 成员跨部门 → 存入 "root" 模块列表，layer = 0
```

### 5.3 TopologySnapshot（运行时拓扑快照）

```
TopologySnapshot
├── Departments[]      ← 部门列表（合并 architecture + modules 数据）
├── Nodes[]            ← 所有模块（GraphNode，含声明+计算双轨）
├── Edges[]            ← 依赖边（GraphEdge，含违规分类）
├── CrossWorks[]       ← 协作小组
├── DepMap / RdepMap   ← 正/反向依赖映射
└── BuiltAt            ← 构建时间
```

### 5.4 ProjectFileNode（文件树扫描结果）

```
ProjectFileNode
├── Name, Path         ← 目录名和相对路径
├── Status             ← Registered / CrossWork / Container / Candidate
├── StatusLabel        ← 显示文案（后端生成，前端直接渲染）
├── Badge              ← 徽章文案（如 "engineering/L2"）
├── Module?            ← 已注册模块的摘要信息
├── Actions            ← 可执行操作（CanRegister / CanEdit / 推荐部门和层级）
└── Children[]         ← 子目录
```

## 六、视界控制（ContextLevel）

视界完全由层级关系动态推导，没有静态的 Boundary 属性。CrossWork 工作组模块拥有特殊访问特权。

| 级别 | 推导条件 | 可见内容 |
|------|---------|---------|
| **Current** | 自己 / activeModules / CW模块→普通模块 | 完全访问 |
| **SharedOrSoft** | 上层合法依赖下层（我依赖你 → 你对我开放） | 完整知识 |
| **CrossWorkPeer** | 同一 CrossWork 的协作方 | 仅该 CrossWork 中声明的 Contract 和 Deliverable |
| **Unlinked** | 同层/反向/无关系/普通→CW/CW→CW | 完全不可见 |

## 七、治理校验

`validate_architecture` 工具执行以下检查：

### 7.1 依赖边校验

| 违规类型 | 判定条件 | 严重程度 |
|----------|---------|---------|
| 循环依赖 | Tarjan SCC 检测 size>1 的强连通分量 | Error |
| 同层直接依赖 | `from.ComputedLayer == to.ComputedLayer`（同 Discipline 内） | Error |
| 反向依赖 | `from.ComputedLayer < to.ComputedLayer` | Error |
| 跨部门直接依赖 | `from.Discipline != to.Discipline` | Error |
| 声明层级未定义 | `module.Layer` 不在 `department.Layers` 中 | Error |
| 声明/计算层偏差 | `module.Layer != module.ComputedLayer` | Warning |

### 7.2 依赖偏差校验

| 检查项 | 说明 |
|--------|------|
| 声明 vs 计算偏差 | `Dependencies`（modules.json）与 `ComputedDependencies`（modules.computed.json）不一致时报出 |
| 仅声明（DeclaredOnly） | 声明了但实际未使用 — 可能是过期声明 |
| 仅计算（ComputedOnly） | 实际存在但未声明 — 可能是遗漏 |

### 7.3 CrossWork 工作组模块校验

| 检查项 | 说明 |
|--------|------|
| 参与方存在性 | 每个 participant.ModuleName 必须在拓扑中 |
| 契约完整性 | 每个参与方必须声明 Contract 或 Deliverable |
| 不应有直接依赖 | CrossWork 参与方之间不应存在 Dependency Edge |
| 普通模块不可依赖 CW | 如果普通模块的 Dependencies 包含 CW 模块名，报违规 |
| CW 模块之间隔离 | CW 模块的 Dependencies 包含另一个 CW 模块名，报违规 |

### 7.4 孤儿检测

没有任何依赖边、也不在任何 CrossWork 中的**普通模块**被标记为孤儿。CrossWork 工作组模块不参与孤儿检测。

## 八、Dashboard 界面

| Tab | 功能 |
|-----|------|
| **架构总览** | 拓扑图可视化 + 部门/层级定义（可折叠编辑区） |
| **工程文件树** | 扫描项目目录，标注四种状态（普通模块/工作组模块/容器/候选），双击候选注册，点击已注册编辑 |
| **知识库** | 记忆 CRUD + 语义检索 |
| **记忆治理** | 鲜活度检查 + 冲突检测 + 归档 |

模块编辑是**公共侧边栏**，从文件树/拓扑/治理报告的任意位置触发。

## 九、MCP 工具清单

| 工具 | 用途 |
|------|------|
| `get_topology` | 查看分层拓扑（按部门组织） |
| `find_modules` | 按名称/关键词检索模块 |
| `get_module_context` | 获取模块上下文（按角色解释、视界过滤） |
| `get_execution_plan` | 拓扑排序执行计划 |
| `begin_task` | 任务开始入口（模块上下文 + 相关 CrossWork） |
| `validate_architecture` | 架构健康检查 |
| `list_crossworks` | 查看 CrossWork 工作组模块及遗留 CrossWork 声明 |
| `remember` | 写入知识记忆（系统标签强制 JSON schema） |
| `recall` | 语义检索记忆 |
