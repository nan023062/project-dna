# Dna.Knowledge.Memory

> 状态：目标重构架构
> 最后更新：2026-04-02
> 适用范围：`src/Dna.Knowledge/Memory`

## 模块定位

`Dna.Knowledge.Memory` 是知识体系中的记忆层。

按照当前最新建模口径，`Memory` 在 `Dna.Knowledge` 这个父级 `Department` 中，应视为一个 `TechnicalNode`：

- 它不是 `Group`
- 它不负责模块导航与层级建模
- 它不负责知识图谱结构定义
- 它不负责治理决策
- 它的唯一核心职责，是把可沉淀的事实、经验、约束和上下文保存为可检索、可回忆、可演化的记忆

一句话概括：

> Memory 回答“哪些工作信息值得稳定保留、它们如何被检索召回、以及它们当前是否仍然可信”。

## 目标类图

`Memory` 的目标类图已拆分到单独文档：

- `src/Dna.Knowledge/Memory/CLASS-DIAGRAM.md`

该文档只负责描述 `Memory` 作为 `TechnicalNode` 的目标模块模型，不承担完整架构说明。

## 模块职责

当前 `Memory` 的目标职责应收敛为：

- 管理短期记忆的存取
- 管理长期记忆的存取
- 管理记忆的增删改查
- 管理记忆的召回、检索、排序与统计
- 管理记忆的时效状态、验证状态、版本与取代关系
- 管理记忆与模块、路径、标签、领域、特性的坐标绑定
- 管理记忆的向量化索引与全文索引
- 为治理层提供稳定的记忆读写基础

这里需要特别强调一条边界：

> `Memory` 管的是“短期记忆和长期记忆”，不是“模块知识”。

也就是说：

- `Memory` 可以保存某个模块相关的经验、约束、教训、活跃事项
- 但这些内容在 `Memory` 中仍然是记忆，不是最终知识
- 最终知识应由治理层沉淀到 `TopoGraph` 中按模块存放

## 非职责范围

`Memory` 不负责：

- 扫描真实工作区目录和文件
- 定义模块父子层级
- 定义模块依赖关系
- 定义模块协作关系
- 注册或持久化 TopoGraph 节点定义
- 保存最终模块知识
- 判定哪些长期记忆应升级为模块知识
- Agent 编排与任务工作流

这意味着：

> `Memory` 是记忆引擎，不是图谱引擎，也不是知识引擎。

## 依赖方向

当前知识域的固定分层依赖是：

```text
Governance
    ->
Memory
    ->
TopoGraph
    ->
Workspace
```

对应到 `Memory` 模块，约束如下：

- `Memory` 允许依赖 `TopoGraph`
- `Memory` 允许间接使用 `Workspace` 提供的事实能力
- `Memory` 不允许反向依赖 `Governance`
- `TopoGraph` 不允许反向依赖 `Memory` 的具体实现

这里的正确理解是：

- `TopoGraph` 负责定义模块结构和模块知识载体
- `Memory` 只引用 `TopoGraph` 的节点身份和模块坐标
- `Governance` 基于 `Memory` 做治理，并把可沉淀结果写入 `TopoGraph`

如果未来 `TopoGraph` 需要读取某个节点的记忆摘要或上下文片段，也必须遵守：

- 由 `TopoGraph` 定义只读契约
- 由 `Memory` 提供适配实现
- 但 TopoGraph 的结构定义与模块知识持久化仍然归 `TopoGraph`

## 核心认知模型

`Memory` 层内部只应明确区分两类对象：

### 1. 短期记忆

短期记忆对应工作过程中的暂存记忆，特点是：

- 和当前任务强相关
- 噪声更高
- 容易过期
- 未必适合长期保留

典型内容包括：

- 当前待办
- 暂时判断
- 正在进行中的任务上下文
- 尚未完全验证的局部观察

### 2. 长期记忆

长期记忆是已经相对稳定、未来可复用的记忆，特点是：

- 经过筛选或验证
- 对未来任务仍有价值
- 可以被多轮工作重复召回
- 可以作为治理输入

典型内容包括：

- 决策
- 约定
- 教训
- 经验摘要
- 稳定事实
- 结构化上下文说明

## 与知识的关系

知识不属于 `Memory` 模块本身。

知识是治理层在更高层面把长期记忆进一步压缩、确认、结构化之后，沉淀到 `TopoGraph` 中的模块知识结果。

这意味着：

```text
Workspace 事实
    ->
短期记忆
    ->
长期记忆
    ->
TopoGraph 中按模块沉淀的知识
```

其中：

- `Workspace` 提供原始事实
- `Memory` 只负责前两段
- `Governance` 负责升级判定与沉淀
- `TopoGraph` 负责按模块承载最终知识

## 核心设计

### 记忆是“内容 + 坐标 + 生命周期”

从目标架构上，`MemoryEntry` 不应该长期维持为一个无边界的大对象。

它应逐步收敛为三块稳定语义：

- `MemoryContent`
  - 记忆正文、摘要、重要度、标签
- `MemoryAddress`
  - 记忆挂接在哪个节点、哪些领域、哪些特性、哪些路径
- `MemoryLifecycle`
  - 记忆当前处于短期还是长期、是否新鲜、是否被验证、是否被取代、当前版本号

这样做的原因是：

- 内容语义和索引语义不是一回事
- 索引坐标和生命周期也不是一回事
- 只有拆开，后续治理与升级策略才不会继续耦合进存储实体本身

### 记忆必须挂接稳定坐标

`Memory` 不是普通文本仓库。
每条记忆都应尽量具备可定位坐标，用于后续召回、治理和记忆升级。

当前推荐坐标包括：

- `NodeType`
- `NodeId`
- `Disciplines`
- `Features`
- `PathPatterns`
- `Tags`

其中：

- `NodeId` 负责对接 `TopoGraph` 中的模块身份
- `PathPatterns` 负责对接 `Workspace` 中的真实文件范围
- `Disciplines / Features / Tags` 负责补充检索和治理维度

这意味着：

> Memory 可以挂接模块，但它不定义模块，也不保存最终模块知识。

### 记忆必须有生命周期

记忆不是“一写入就永远正确”的静态文档。
它必须显式管理生命周期，至少包括：

- 创建时间
- 最近验证时间
- 预期过期时间
- 新鲜度状态
- 版本号
- 被新记忆取代的链路

当前新鲜度应继续围绕这些状态演进：

- `Fresh`
- `Aging`
- `Stale`
- `Superseded`
- `Archived`

后续如果需要区分短期和长期记忆，应使用独立的 `MemoryStage` 或等价概念，而不是继续滥用 `Freshness` 字段承载两种语义。

### 召回是混合检索，不是单一搜索

`Memory` 的召回不应退化成简单全文搜索。
它应继续保持混合召回架构：

- 向量语义检索
- 全文检索
- 标签匹配
- 坐标过滤
- 新鲜度过滤
- 重要度加权

也就是说：

> Recall 的本质是“在记忆坐标和生命周期约束下，对候选记忆做融合排序”。

### 系统标签属于 Memory，不属于 TopoGraph

像下面这些结构化记忆标签：

- `#identity`
- `#lesson`
- `#active-task`

本质上都是记忆层自己的结构化内容类型。

它们可以被上层模块消费，但语义归属仍然在 `Memory`：

- `#identity`
  - 某个模块当前的身份性记忆摘要
- `#lesson`
  - 某次经验与教训
- `#active-task`
  - 当前活动事项

这类结构化 payload 应继续放在 `Memory` 中管理，而不是被 `TopoGraph` 重新吸收成图谱定义字段。

## 内部组件分层

当前 `Memory` 内部建议收敛成下面这组组件：

### 1. `IMemoryEngine` / `MemoryEngine`

模块门面。

职责：

- 作为上层唯一稳定入口
- 组织读、写、召回、统计、治理辅助接口
- 不直接承载 SQLite 细节或向量细节

### 2. `MemoryWriter`

写入服务。

职责：

- 写入前校验
- 生成记忆 ID
- 解析系统标签 payload
- 生成 embedding
- 持久化到存储
- 更新向量索引

### 3. `MemoryReader`

读取服务。

职责：

- 结构化查询
- 统计汇总
- 约束链读取
- 面向领域的摘要查询

### 4. `MemoryRecallEngine`

召回服务。

职责：

- 混合检索
- 候选合并
- 融合排序
- 新鲜度过滤
- 上下文扩展

### 5. `MemoryStore`

底层存储仓库。

职责必须收敛为：

- 只管理记忆数据的持久化
- 只管理记忆相关索引表
- 只管理记忆查询所需的底层 SQL 能力

它不应继续承载：

- 图谱节点定义
- 模块注册清单
- 图谱计算结果
- TopoGraph 主树快照
- 最终模块知识

### 6. `EmbeddingService`

向量服务适配器。

职责：

- 调用外部 embedding 模型
- 对上层暴露统一 embedding 能力
- 在未配置时静默降级

### 7. `VectorIndex`

内存向量索引。

职责：

- 管理向量缓存
- 执行相似度检索
- 为召回服务提供近似语义候选

## 对外能力

`Memory` 当前对上层应稳定暴露这些能力：

- `RememberAsync(...)`
- `UpdateMemoryAsync(...)`
- `DeleteMemory(...)`
- `VerifyMemory(...)`
- `RecallAsync(...)`
- `QueryMemories(...)`
- `GetMemoryById(...)`
- `GetConstraintChain(...)`
- `GetFeatureSummary(...)`
- `GetDisciplineSummary(...)`
- `GetMemoryStats()`
- `DecayStaleMemories()`

可以看到：

- 对外是“记忆服务接口”
- 不是“图谱定义服务接口”
- 也不是“模块知识服务接口”

## 与其他模块的边界

### `Workspace` 负责

- 真实目录和文件事实
- 安全路径读写
- 目录元数据
- 工作区变化监听

### `Memory` 负责

- 事实、经验、约束、上下文的记忆化
- 短期记忆与长期记忆的存取
- 记忆的坐标绑定
- 记忆的生命周期管理
- 记忆的召回与检索

### `TopoGraph` 负责

- 模块节点定义
- 父子层级
- 依赖关系
- 协作关系
- 模块知识的最终存放与组织
- 模块到物理路径的结构解释

### `Governance` 负责

- 判断哪些短期记忆应转为长期记忆
- 判断哪些长期记忆应沉淀为模块知识
- 管理压缩、淘汰、归档和升级流程

这四层必须始终维持单向依赖和清晰边界。

## 当前实现与目标架构的差异

当前实现里，最主要的问题不是检索算法，而是职责混装：

### 1. `MemoryStore` 过胖

它现在同时承担了：

- 记忆 SQLite 存储
- 向量与 FTS 相关底层能力
- 旧 TopoGraph 模块清单存储
- 旧图谱快照存储
- 节点知识汇总存储

这违反了单一职责。

### 2. 图谱定义、模块知识与记忆定义混在一起

当前 `MemoryStore` 中的这些内容应从核心记忆存储中移出：

- `ArchitectureManifest`
- `ModulesManifest`
- `ComputedManifest`
- `graph_modules`
- `graph_disciplines`
- `graph_crossworks`
- `graph_features`
- 节点级知识快照

这些都属于 `TopoGraph` 的定义域，而不是 `Memory` 的定义域。

### 3. `BuildInternals(...)` 暴露出过多装配细节

当前 `MemoryStore` 同时承担了存储与内部服务构建职责。

长期看应收敛为：

- `MemoryEngine` 作为门面
- `MemoryStore` 作为仓库
- 其他服务作为内部协作者

而不是继续把整个模块组装逻辑塞在存储层里。

## 当前重构建议

后续 `Memory` 重构建议按以下顺序推进：

1. 先把 `MemoryStore` 中的图谱定义与模块知识存储彻底迁出到 `TopoGraph`
2. 保留 `MemoryStore` 只负责记忆表、索引表和记忆查询
3. 如确有需要，为 `TopoGraph` 提供一个单独的只读记忆上下文适配器，而不是继续让整个 `MemoryStore` 承担图谱适配职责
4. 逐步把 `MemoryEntry` 从大平铺对象收敛为“内容 + 坐标 + 生命周期”结构
5. 在治理层引入显式的“短期记忆 -> 长期记忆 -> 模块知识”升级流程，而不是把升级逻辑塞回 `Memory`

## 当前架构结论

`Memory` 的正确定位不是“杂糅了图谱、记忆、治理兼容逻辑的大仓库”，而是：

> 一个面向短期记忆与长期记忆的 `TechnicalNode`，负责记忆的存取、召回、索引、生命周期管理和上层治理支撑，但不负责 TopoGraph 结构定义，也不负责最终模块知识存放。

后续 `Memory` 重构，应以这份文档作为边界约束。