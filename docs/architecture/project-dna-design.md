# 工程 DNA 系统（Project DNA）：大道至简，万物归一

> **状态**：设计稿 v1.3 — 2026-03-28
> **作者**：nave
> **范围**：Agentic OS 工程 DNA 系统的核心模型与重构方向

---

## 一、设计哲学

### 1.0 目的

**工程 DNA 系统是对项目工程文件系统的内容知识与关系的提炼。**

项目文件系统有成千上万个文件，Agent 不可能每次都从头遍历理解。
工程 DNA 把文件系统的结构、知识、关系编码为可查询的图谱——
Agent 读 DNA 就能快速获取上下文、理解依赖、遵循流程规范、编排任务、精准修改目标文件。

```
物理层（文件系统）       认知层（工程 DNA）           行动层（Agent）
┌──────────────┐      ┌──────────────────┐      ┌────────────────┐
│ 几千个文件    │─提炼→│ 几十个带知识的节点 │─服务→│ 精准操作目标文件 │
│ 目录/代码/资源 │      │ 依赖/协作/约束    │      │ 带上下文修改     │
└──────────────┘      └──────────────────┘      └────────────────┘
```

DNA 不是生物体本身，而是生物体的蓝图。
工程 DNA 不是文件本身，而是文件系统的**认知编码**。

| 生物 DNA | 工程 DNA |
|----------|----------|
| 基因片段编码蛋白质功能 | 节点编码模块的职责、约束、契约 |
| 基因之间有调控关系 | 节点之间有依赖和协作关系 |
| DNA 指导细胞如何工作 | DNA 指导 Agent 如何操作文件 |
| 突变会被修复机制检测 | 违反约束会被治理引擎发现 |
| DNA 可以遗传和复制 | 知识可以沉淀、传承和复用 |

### 1.1 核心思想

**组织架构与软件架构是同构的（Isomorphic）。**

所有工种、团队、协作关系都遵循与程序架构相同的设计原则。
工程 DNA 不是「代码的附属品」，而是用软件架构思维建模的**项目认知网络**。

### 1.2 基本公理

| 公理 | 软件架构 | 组织形态 |
|------|----------|----------|
| **内聚** | 模块单一职责、高内聚低耦合 | 专一工种（战斗程序、角色美术） |
| **契约** | 接口/API 对外稳定暴露 | 团队对外承诺的交付物和 SLA |
| **依赖** | 单向依赖、禁止循环 | 消费关系：我用你的服务/数据 |
| **协作** | 组合模式，中介者协调 | 工作专班/小组共同交付复合目标 |
| **聚合** | 包/命名空间逻辑分组 | 部门/大组管理多个小组 |
| **治理** | DAG 校验、架构建议 | 架构顾问、组织健康度检查 |

### 1.3 三条铁律

| # | 规则 | 含义 |
|---|------|------|
| 1 | **包含是树** | 每个节点最多一个 Parent，层级由树深度决定 |
| 2 | **依赖是 DAG** | 任意节点间可建立单向依赖，不限组织边界，只要不成环 |
| 3 | **环路 = 该合并** | 循环依赖说明这些节点实为一个内聚体，应合并或组建小组 |

依赖和协作是两个正交维度：

| 概念 | 本质 | 方向 | 举例 |
|------|------|------|------|
| **依赖**（Edge） | 「我需要用你的输出」 | 单向消费 | 战斗程序读技能配表 |
| **协作**（CrossWork） | 「我们一起交付一个东西」 | 多方共建 | 战斗组：策划+程序+美术+音效 |

依赖可以自由穿越组织边界，就像 `import` 可以引用任何包一样——只要别成环。
CrossWork 是主动声明的协作体，不是依赖的替代品。

### 1.4 分形原则

每个层级——从项目根到最小模块——都是**同一个模式的递归实例**。
一个部门和一个模块具有完全相同的属性结构，区别仅在于粒度和包含关系。

```
工作室 ⊃ 部门 ⊃ 小组/专班 ⊃ 模块
  │        │        │          │
  └────────┴────────┴──────────┘
         同一种节点，不同尺度
```

---

## 二、统一节点模型

### 2.1 KnowledgeNode — 唯一的图节点类型

抛弃 `GraphNode` / `CrossWork` / `Department` 三种不同数据结构。
**图中只有一种节点 `KnowledgeNode`**，通过 `NodeType` 和层级关系表达所有组织形态。

```csharp
public class KnowledgeNode
{
    // ── 身份 ──
    public string Id { get; set; }
    public string Name { get; set; }
    public NodeType Type { get; set; }

    // ── 层级关系（树：包含） ──
    public string? ParentId { get; set; }
    public List<string> ChildIds { get; set; } = [];
    // 层级不需要显式声明——节点到 Root 的树深度就是它的层级。

    // ── 依赖关系（有向无环图） ──
    public List<string> Dependencies { get; set; } = [];
    // 依赖可以跨越任何组织边界，唯一约束是不能成环。

    // ── 契约 ──
    public string? Contract { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }

    // ── 结构属性 ──
    public string? RelativePath { get; set; }
    public string? Maintainer { get; set; }
    public string? Boundary { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    // ── 内嵌知识（物化视图） ──
    public NodeKnowledge Knowledge { get; set; } = new();
}

public enum NodeType
{
    Root,         // 项目 / 工作室
    Department,   // 部门 / 大组
    Module,       // 内聚模块 / 工种
    CrossWork     // 协作专班 / 工作小组
}
```

### 2.2 NodeKnowledge — 节点自带的知识载体

每个节点——无论类型——都自带结构化知识。
这是从记忆引擎投影的**物化视图**，不是记忆的全量拷贝。

```csharp
public class NodeKnowledge
{
    public string? Identity { get; set; }
    public string? Contract { get; set; }
    public List<LessonSummary> Lessons { get; set; } = [];
    public List<string> ActiveTasks { get; set; } = [];
    public List<string> Facts { get; set; } = [];
    public int TotalMemoryCount { get; set; }
    public List<string> MemoryIds { get; set; } = [];
}

public class LessonSummary
{
    public string Title { get; set; } = string.Empty;
    public string? Severity { get; set; }
    public string? Resolution { get; set; }
}
```

### 2.3 四种节点类型的组织映射

| NodeType | 软件类比 | 组织类比 | 举例 |
|----------|----------|----------|------|
| `Root` | System / Application | 工作室 / 项目 | 「XX 手游项目」 |
| `Department` | Package / Namespace | 部门 / 大组 | 技术中台、美术中心 |
| `Module` | Class / Service | 内聚工种 / 小组 | 战斗程序、角色美术 |
| `CrossWork` | Composite / Mediator | 工作专班 / 联合小组 | 战斗组（含策划+程序+美术+音效） |

层级举例：

```
Root: 游戏工作室
├── Department: 技术部
│   ├── Module: 引擎支撑组
│   ├── Module: 前端组
│   ├── Module: 后端组
│   └── Module: DevOps
├── Department: 美术部
│   ├── Module: 角色组
│   ├── Module: 场景组
│   └── Module: 特效组
├── Department: 策划部
│   ├── Module: 战斗策划
│   ├── Module: 系统策划
│   └── Module: 关卡策划
└── CrossWork: 战斗系统专班
    ├── Participant: 战斗策划
    ├── Participant: 战斗程序（前端组）
    ├── Participant: 角色美术（角色组）
    └── Participant: 战斗音效
```

---

## 三、文件系统到 DNA 的映射

### 3.1 核心规则

文件系统是物理结构（文件夹嵌套 + 文件），DNA 是认知结构（节点 + 关系）。
映射规则只有两条：

| 文件系统 | DNA 节点类型 | 原因 |
|----------|-------------|------|
| 有子目录的文件夹 | **Department** | 它是容器，不是实现 |
| 只有文件的文件夹 | **Module** | 它是最小实现单元 |

**关键原则：一个节点要么是容器（Department），要么是实现（Module），不能同时兼任。**

如果一个文件夹**既有子目录又有散落文件**，散落文件必须提升为独立 Module：

```
文件系统:                          DNA:
Battle/                            Battle (Department — 纯容器)
├── Skill/                         ├── Unit (Module, path: Battle/*.cs)
│   └── SkillCaster.cs             ├── Skill (Module, path: Battle/Skill/)
├── Buff/                          ├── Buff (Module, path: Battle/Buff/)
│   └── BuffManager.cs             └── Attribute (Module, path: Battle/Attribute/)
├── Attribute/
│   └── AttributeSystem.cs        依赖：
├── Unit.cs          ← 散落文件    Unit → Skill
├── UnitFactory.cs   ← 散落文件    Unit → Buff
└── BattleManager.cs ← 散落文件    Unit → Attribute
```

为什么不把散落文件归入 Battle 父节点？因为会导致「父模块依赖子模块」的困惑。
将散落文件提升为 Module 后，所有依赖都是**兄弟之间的单向关系**，干净明了。

### 3.2 代码工程示例

```
src/                                   Root: MyGame
├── Battle/                            ├── Battle (Department)
│   ├── Skill/                         │   ├── Unit (Module) ──→ Skill, Buff, Attribute
│   │   ├── Active/                    │   ├── Skill (Department)
│   │   └── Passive/                   │   │   ├── ActiveSkill (Module)
│   ├── Buff/                          │   │   └── PassiveSkill (Module)
│   ├── Attribute/                     │   ├── Buff (Module)
│   ├── Unit.cs                        │   └── Attribute (Module)
│   └── BattleManager.cs              │
├── UI/                                ├── UI (Module)
│   └── UIManager.cs                   │
└── Network/                           └── Network (Module)
    └── NetClient.cs
```

规则递归应用：`Skill/` 下面又有 `Active/` 和 `Passive/` 子目录 → Skill 升级为 Department，子目录各为 Module。

### 3.3 美术资源示例

同样的规则适用于美术资源：

```
Art/                                   Art (Department — 美术部)
├── Characters/                        ├── ArtCommon (Module, path: Art/*.pdf)
│   ├── Hero/                          │   knowledge: "美术通用规范、图集配置"
│   │   ├── Mesh/                      ├── Characters (Department — 角色组)
│   │   ├── Texture/                   │   ├── Hero (Module)
│   │   └── Animation/                 │   │   knowledge: "面数≤8000, 骨骼≤60"
│   ├── Monster/                       │   ├── Monster (Module)
│   └── NPC/                           │   └── NPC (Module)
├── VFX/                               ├── VFX (Department — 特效组)
│   ├── Skill/                         │   ├── SkillVFX (Module)
│   └── UI/                            │   └── UIVFX (Module)
├── ArtStandard.pdf    ← 散落文件      │
└── TextureAtlas.asset ← 散落文件      依赖：Hero → SkillVFX (主角需要技能特效)
```

美术节点的 Knowledge 存的是美术规范：

```
Hero 节点:
  Identity: "主角角色资产，负责 Mesh/Texture/Animation"
  Constraints: ["面数≤8000", "骨骼≤60", "贴图最大1024x1024"]
  Contract: "输出 FBX+PNG，命名 hero_{name}_{variant}"
  Lessons: ["hero_warrior 的 IK 骨骼多了导致移动端掉帧"]
```

### 3.4 策划配表示例

```
Design/                                Design (Department — 策划部)
├── Tables/                            ├── DesignCommon (Module, path: Design/*.md)
│   ├── Skill/                         ├── Tables (Department — 数值组)
│   │   ├── SkillConfig.xlsx           │   ├── TablesCommon (Module, path: Design/Tables/*.xlsx)
│   │   └── SkillEffect.xlsx           │   ├── SkillTable (Module)
│   ├── Buff/                          │   │   knowledge: "字段: id/name/cooldown/damage"
│   │   └── BuffConfig.xlsx            │   ├── BuffTable (Module)
│   ├── Monster/                       │   └── MonsterTable (Module)
│   │   └── MonsterConfig.xlsx         ├── Documents (Department — 系统策划)
│   └── GlobalConfig.xlsx ← 散落      │   ├── CombatDesign (Module)
├── Documents/                         │   └── EconomyDesign (Module)
│   ├── CombatDesign.docx              └── LevelDesign (Department — 关卡策划)
│   └── EconomyDesign.docx                 ├── Chapter1 (Module)
├── LevelDesign/                           └── Chapter2 (Module)
│   ├── Chapter1/
│   └── Chapter2/                      依赖：
└── DesignGuideline.md ← 散落         SkillTable → BuffTable (技能表引用 Buff ID)
                                       MonsterTable → SkillTable (怪物表引用技能 ID)
                                       Chapter1 → MonsterTable (关卡引用怪物配置)
```

### 3.5 跨职能 CrossWork 示例

真正体现 DNA 价值的是跨职能协作——程序、美术、策划的节点各在自己的 Department 下，通过 CrossWork 关联：

```
Root: MyGame
├── Tech (Department)
│   └── BattleCode (Module) ──→ SkillTable   ← 程序读策划配表
├── Art (Department)
│   └── SkillVFX (Module)
├── Design (Department)
│   └── SkillTable (Module)
│
└── CrossWork: 火球术专班
    ├── Participant: SkillTable  — 策划出数值和表现描述
    ├── Participant: BattleCode  — 程序实现释放逻辑
    ├── Participant: SkillVFX    — 美术做火焰特效
    └── Knowledge:
        ├── "策划先出配表 → 程序接入 → 美术出特效 → 联调"
        ├── "特效播放时长必须和技能前摇时间一致"
        └── "上次爆炸范围和配表不一致，配表用厘米程序用米"
```

### 3.6 通用映射规则总表

| 规则 | 代码 | 美术 | 策划 |
|------|------|------|------|
| 有子目录 → Department | `Battle/` | `Characters/` | `Tables/` |
| 纯文件目录 → Module | `Buff/` | `Hero/` | `SkillTable/` |
| 散落文件 → 提升为 Module | `Unit.cs` | `ArtStandard.pdf` | `GlobalConfig.xlsx` |
| Knowledge 内容 | 代码规范、GC 约束 | 面数/骨骼/贴图规格 | 字段规范、引用关系 |
| 兄弟依赖 | `Unit → Skill` | `Hero → SkillVFX` | `SkillTable → BuffTable` |
| 跨职能协作 → CrossWork | 程序实现逻辑 | 美术出资源 | 策划出配表 |

**一套规则，所有工种，同一个 DNA。**

---

## 四、图的三种关系

图中只有三种关系，完全正交、互不干扰：

- **树**管组织归属
- **DAG**管谁用谁
- **CrossWork**管谁和谁一起干活

### 3.1 包含关系（树）

通过 `ParentId` / `ChildIds` 表达聚合层级。

- Root → Department → Module（组织树）
- 一个节点只能有一个 Parent（树约束）
- 层级 = 节点到 Root 的树深度，不需要额外字段
- CrossWork 的 ChildIds 引用参与节点（引用关系，不改变参与方的 Parent）

### 3.2 依赖关系（有向无环图）

通过 `Dependencies` 列表和独立的 `Edge` 实体表达单向依赖。

```csharp
public class KnowledgeEdge
{
    public string From { get; init; } = string.Empty;
    public string To { get; init; } = string.Empty;
    public bool IsComputed { get; init; }
}
```

Edge 没有类型字段——**边就是边**，表示「From 消费 To 的服务/输出」。

**规则**：

- 依赖可以**自由穿越组织边界**（跨 Parent、跨 Department 均合法）
- 唯一约束：**全图无环**（DAG）
- 如果检测到环路 → 不是报错，而是**重构建议**：这些节点应该合并或组建小组
- Edge 是一等公民，可独立增删

**示例**：跨部门依赖完全合法

```
技术部                  策划部
├── 战斗程序 ──→ 引擎组   ├── 战斗策划
│       │                     ↑
│       └─────────────────────┘   ← 跨部门依赖，合法
└── UI 框架 ──→ 引擎组   └── 系统策划
```

### 3.3 协作关系（CrossWork）

CrossWork 不是依赖的替代品，而是**多方共建的声明**。

- 当多个节点需要共同交付一个业务目标时，成立 CrossWork
- CrossWork 节点自身不产生依赖边
- CrossWork 节点自带协作知识（契约、联调教训、时序约束等）
- 简单的「我用你的输出」不需要 CrossWork，一条 Edge 就够了

**何时用 Edge vs CrossWork**：

| 场景 | 用什么 |
|------|--------|
| 战斗程序读技能配表 | Edge（单向消费） |
| 战斗组：策划+程序+美术+音效共同交付战斗系统 | CrossWork（多方共建） |
| 后端依赖数据库中间件 | Edge（单向消费） |
| 新手引导专班：策划定流程+程序实现+QA 验收 | CrossWork（多方共建） |

---

## 五、记忆归属：一条记忆属于一个节点

### 4.1 告别多对多

旧模型：`MemoryEntry.ModuleIds: List<string>` — 一条记忆关联多个模块，归属模糊。

新模型：`MemoryEntry.NodeId: string` — 每条记忆严格归属于图中一个节点。

### 4.2 归属规则

| 记忆性质 | 归属节点 | 举例 |
|----------|----------|------|
| 模块内部知识 | 该 Module 节点 | 「BattleSystem 禁止在 Update 中 new 对象」 |
| 跨模块协作知识 | CrossWork 节点 | 「火球术：伤害计算完成后必须等 VFX 回调才扣血」 |
| 部门级规范 | Department 节点 | 「技术部代码审查必须 2 人以上」 |
| 项目级全局知识 | Root 节点 | 「项目使用 Unity 2022.3 LTS，最低帧率 30fps」 |

### 4.3 物化视图机制

写入路径：

```
Client → remember(nodeId, content)
       → MemoryEngine 存记忆
       → 通知 GraphEngine 刷新该节点的 Knowledge 摘要
```

读取路径：

```
Client → get_node(name)
       → 返回 KnowledgeNode（含 Knowledge 摘要，一次调用满足 80% 场景）

Client → recall(question, nodeId)
       → MemoryEngine 语义搜索，返回详细记忆（深度查询）
```

### 4.4 依赖方优先原则

当一条知识涉及两个有依赖关系的模块时（非 CrossWork），归属**依赖发起方**（From 节点），因为它是「踩坑者」。
如果该知识描述的是长期协作约定，系统建议升级为 CrossWork。

---

## 六、架构原则同构表

### 5.1 结构型

| 软件原则 | 图谱体现 | 治理建议 |
|----------|----------|----------|
| 单一职责（SRP） | 每个 Module 节点只负责一个内聚领域 | 节点 Knowledge 内容是否超出声明职责 |
| 开闭原则（OCP） | Contract 稳定对外，内部可扩展 | Contract 变更需走审批流 |
| 依赖倒置（DIP） | 依赖指向的是 Contract，不是内部实现 | 提示依赖了无 Contract 的节点 |
| 接口隔离（ISP） | PublicApi 只暴露必要接口 | 接口膨胀检测 |

### 5.2 关系型

| 软件原则 | 图谱体现 |
|----------|----------|
| DAG 约束 | 全图无环，Tarjan SCC 检测 |
| 环路 = 内聚不足 | 检测到环 → 建议合并为一个节点或组建小组 |
| 树形归属 | Parent-Child 包含关系，层级 = 树深度 |
| 最小知识原则（LoD） | 视界控制：非依赖链/非 CrossWork 同组的节点不可见 |

### 5.3 协作型

| 软件模式 | 组织映射 | 图谱体现 |
|----------|----------|----------|
| 中介者（Mediator） | 项目经理、制作人 | CrossWork 节点 |
| 外观（Facade） | 技术总监、对外接口人 | Department 节点的 Contract |
| 适配器（Adapter） | TA（技术美术） | IContextInterpreter 按角色翻译上下文 |
| 观察者（Observer） | 周报/站会/变更通知 | 记忆写入触发节点 Knowledge 刷新 |
| 事件驱动 | 里程碑触发流程 | ExecutionPlan 拓扑排序 |

### 5.4 治理型

治理引擎的定位是**架构顾问**而非规则警察——给建议，不报违规。

| 检测 | 建议 |
|------|------|
| A ↔ B 形成环 | 「A 和 B 紧密耦合，建议合并为一个节点或创建小组」 |
| 某节点被大量节点依赖 | 「这是关键基础节点，注意契约稳定性」 |
| 某 CrossWork 参与方之间无交互 | 「这个专班可能可以拆小」 |
| 记忆过期未验证 | 「以下知识可能已过时，建议确认」 |
| 同一节点存在矛盾知识 | 「以下记忆可能冲突，建议核实」 |

其他治理映射：

| 软件实践 | 组织映射 | 图谱体现 |
|----------|----------|----------|
| 架构评审 | 技术评审会 | GovernanceEngine.Validate() |
| 健康检查 | 团队复盘/绩效评估 | FreshnessChecker |
| 熔断器 | 切断问题组避免拖垮全局 | 节点 blocked 状态 |
| 版本管理 | 流程/规范向前兼容迭代 | MemoryEntry.Version + EvolutionChain |
| 可观测性 | 项目周报、知识沉淀 | 记忆系统本身 |

---

## 七、工程 DNA 三引擎架构

将当前的 `KnowledgeGraph` 上帝类拆为三个正交子系统：

系统最终由**两个独立项目**组成：后端（纯知识 API）和前端（Dashboard）。

```
┌─────────────────────────────────────────────────────┐
│         DNA Server（后端，纯知识服务）                 │
│         不访问项目文件系统，可部署在任何地方            │
│                                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────┐  │
│  │ GraphEngine  │  │ MemoryEngine │  │ Governance │  │
│  │              │  │              │  │   Engine   │  │
│  │  节点 CRUD   │  │  记忆读写     │  │  治理分析  │  │
│  │  边 CRUD     │  │  语义检索     │  │  鲜活度    │  │
│  │  拓扑计算    │  │  向量索引     │  │  冲突检测  │  │
│  │  执行计划    │  │  导入导出     │  │  偏差检测  │  │
│  └──────┬───────┘  └──────┬───────┘  └─────┬─────┘  │
│         └─────────────────┴────────────────┘        │
│                    StorageLayer                       │
│         SQLite + JSON + Manifest 文件                 │
│              ~/.dna/projects/{name}/                  │
│                                                     │
│  对外接口: MCP (stdio/SSE) + REST API                 │
└─────────────────────────────────────────────────────┘
        ▲ REST / MCP            ▲ REST
        │                       │
┌───────┴────────┐    ┌────────┴──────────────────────┐
│  Agent Client  │    │   Dashboard（前端，独立项目）    │
│  (Cursor/Codex)│    │                               │
│                │    │  ┌─────────────────────────┐   │
│  读写项目文件   │    │  │ 知识库可视化              │   │
│  通过 MCP 读写  │    │  │  拓扑图 / 记忆列表 / 治理 │   │
│  DNA 知识      │    │  └─────────────────────────┘   │
└────────────────┘    │  ┌─────────────────────────┐   │
                      │  │ 项目管理（可选）          │   │
                      │  │  指定项目工程路径         │   │
                      │  │  文件树浏览              │   │
                      │  │  扫描项目 → 写入 DNA      │   │
                      │  └─────────────────────────┘   │
                      └────────────────────────────────┘
```

**核心原则**：DNA Server 是纯知识服务，不访问项目文件系统。所有知识通过 API 写入。
Dashboard 是可选的管理前端，可以连接本地项目工程辅助扫描建图。

### 6.1 GraphEngine

图的全部生命周期管理。节点和边是一等公民，支持增量拓扑更新。

**职责**：
- 节点 CRUD（add / get / update / remove / list）
- 边 CRUD（add / remove / query）
- 拓扑快照（全量 / 增量）
- 执行计划（拓扑排序）
- 上下文构建（视界过滤 + 节点 Knowledge 直取）

**存储**：
- `graph.json` — 统一的节点与边数据（取代旧的 architecture.json + modules.json）

### 6.2 MemoryEngine

知识条目的存储与检索。纯粹管 MemoryEntry，不再掺杂 manifest 管理。

**职责**：
- 记忆写入（remember / batch_remember / update / delete）
- 语义检索（recall — 四通道检索 + 融合排序 + 约束链展开）
- 结构化查询（query — 按坐标过滤）
- 索引维护（rebuild / sync / export）

**写后回调**：写入成功后通知 GraphEngine 刷新目标节点的 Knowledge 摘要。

**存储**：
- `memory/index.db` — SQLite 索引
- `memory/entries/*.json` — Git 友好的知识内容

### 6.3 GovernanceEngine

架构顾问——组合 GraphEngine + MemoryEngine 的数据，给出重构建议而非报违规。

**职责**：
- 环路检测（Tarjan SCC）→ 建议合并或组建小组
- 关键节点识别（被大量依赖的基础节点）→ 提示契约稳定性
- CrossWork 健康度（参与方是否有实际交互）→ 建议拆分或解散
- 鲜活度检查 + 过期记忆衰减/归档
- 冲突检测（同一节点的矛盾知识）

---

## 八、MCP 工具表面

### 7.1 GraphTools

| 工具 | 说明 |
|------|------|
| `get_project_identity` | 校验项目身份 |
| `add_node` | 添加节点（任意类型） |
| `get_node` | 获取节点（含内嵌知识） |
| `update_node` | 更新节点属性 |
| `remove_node` | 删除节点 |
| `list_nodes` | 列出节点（支持按类型/部门过滤） |
| `add_edge` | 添加单向依赖 |
| `remove_edge` | 删除依赖 |
| `get_topology` | 获取完整拓扑快照 |
| `get_execution_plan` | 获取拓扑排序执行计划 |
| `begin_task` | 获取模块上下文（视界过滤） |
| `find_modules` | 搜索节点 |

### 7.2 MemoryTools

| 工具 | 说明 |
|------|------|
| `remember` | 写入一条记忆（归属指定 NodeId） |
| `recall` | 语义检索 |
| `batch_remember` | 批量写入 |
| `update_memory` | 更新记忆 |
| `delete_memory` | 删除记忆 |
| `query_memories` | 结构化查询 |
| `get_memory` | 按 ID 获取 |
| `verify_memory` | 验证记忆仍有效 |
| `rebuild_index` | 重建索引 |
| `export_to_json` | 导出 |

### 7.3 GovernanceTools

| 工具 | 说明 |
|------|------|
| `validate_architecture` | 架构合规检查 |
| `get_memory_stats` | 知识库统计 |
| `check_freshness` | 鲜活度检查 |

---

## 九、两个项目的职责划分

### 9.1 DNA Server（后端）

纯知识服务，不访问项目文件系统。可部署在本地、内网服务器或云端。

**对外接口**：
- MCP（stdio / SSE）— 供 Agent Client（Cursor / Codex / Claude Code）接入
- REST API — 供 Dashboard 和其他工具调用

**职责边界**：
- 知识 CRUD（节点、边、记忆、CrossWork）
- 拓扑计算与查询
- 治理分析（环路检测、鲜活度、冲突检测）
- 知识存储管理（`~/.dna/projects/{name}/`）

**不做的事**：
- 不读写项目工程文件
- 不扫描源码
- 不推导代码依赖

### 9.2 Dashboard（前端，独立项目）

Web 前端 + 轻量 API，连接 DNA Server 展示和管理知识。

**职责**：
- 知识库可视化（拓扑图、记忆列表、治理报告）
- 知识编辑（通过 DNA Server REST API）
- 项目管理（可选，需指定本地项目路径）：
  - 文件树浏览
  - 项目扫描 → 生成节点和依赖 → 写入 DNA Server
  - Scanner 插件运行环境

### 9.3 Agent Client（IDE 内的 AI Agent）

通过 MCP 协议连接 DNA Server。

- 对话开始 → `get_project_identity` → `begin_task`
- 修改代码前 → 从节点 Knowledge 获取约束
- 完成后 → `remember` 写回知识
- Agent 直接读写项目文件（工作区），通过 MCP 读写知识（DNA Server）

---

## 十、重构路线

### Phase 1：统一节点模型 + 拆上帝类（不拆进程）

1. 统一 `GraphNode` / `CrossWork` / `Department` → `KnowledgeNode`
2. 删除 `Layer` 字段，层级由树深度隐式决定
3. 删除 `EdgeKind` 枚举（旧的五种违规类型），边就是边
4. 从 `MemoryStore.Facade` 抽出 manifest 管理 → `GraphStore`
5. 新建 `GraphEngine : IGraphEngine`
6. 新建 `MemoryEngine : IMemoryEngine`
7. 新建 `GovernanceEngine : IGovernanceEngine`（顾问模式，非警察模式）
8. `MemoryEntry.ModuleIds` → `MemoryEntry.NodeId`
9. 现有 MCP 工具类改为注入三引擎接口
10. 保持单体运行，所有功能不变

### Phase 2：Edge 一等公民 + 节点自带知识

1. Edge 独立 CRUD（不再通过修改 Dependencies 间接实现）
2. 环路检测改为重构建议（Tarjan SCC → 建议合并/组建小组）
3. 节点 `Knowledge` 物化视图实现
4. 记忆写入后自动刷新节点 Knowledge
5. `begin_task` / `get_module_context` 简化为直接读节点

### Phase 3：Server 纯化（去除文件系统依赖）

1. `ProjectRoot` 在 Server 侧退化为标识符，不用于读文件
2. `IProjectAdapter`、`ProjectScanner`、`ProjectTreeCache` 移到 Dashboard 项目
3. `FileTreeEndpoints` 移到 Dashboard
4. `FreshnessChecker` 简化为纯时效检查（不检查文件变更）
5. Server 可在无项目路径的情况下完整工作

### Phase 4：Dashboard 独立项目

1. 新建 Dashboard 前端项目（独立仓库或 monorepo 子目录）
2. 知识库可视化（拓扑图、记忆列表、治理报告）
3. 项目管理功能（文件树浏览、项目扫描）
4. Scanner 插件运行环境

### Phase 5：增量拓扑 + 性能优化

1. 节点变更触发增量拓扑更新
2. 脏检查 + 延迟重算
3. 批量操作优化

---

## 十一、一句话总结

> **大道至简，万物归一。**
>
> 图中只有**一种节点**（KnowledgeNode），每个节点自带知识。
> 节点之间只有**三种关系**：包含（树）、依赖（DAG）、协作（CrossWork）——完全正交。
> 层级 = 树深度，无需声明；依赖自由穿越组织边界，唯一禁令是不能成环；
> 环路不是违规，而是重构信号——循环依赖的节点本质上是一个内聚体。
> 所有组织形态——模块、专班、部门、工作室——都是同一个模式在不同尺度上的递归。
>
> 如果两个人总是需要坐在一起才能干活，那他们就该在同一个工位。
> 如果两个组经常需要协作，就成立一个专班。
> 如果你发现自己在画环形依赖图，说明组织边界画错了。
