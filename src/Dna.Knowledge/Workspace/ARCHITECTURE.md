# Dna.Knowledge.Workspace

## 模块定位

`Dna.Knowledge.Workspace` 是知识体系中的最底层模块。
它直接对应真实物理工作区，并把真实文件系统视为唯一事实来源。

按照当前最新建模口径，`Workspace` 在 `Dna.Knowledge` 父级模块中属于一个 `TechnicalNode`：

- 它是稳定复用的基础技术能力
- 它不承担 `Group` 的分组导航职责
- 它不承担 `Team` 的业务闭环职责
- 它的唯一核心职责是向上提供工作区事实能力

`Workspace` 不是另一个抽象的项目模型。
它是在真实文件系统之上增加一层稳定包装，补上这些基础能力：

- 实时扫描与监听
- 工作区安全路径解析
- 目录与文件索引
- 受限读写能力
- 基于 `.agentic.meta` 的目录级元数据

一句话概括：

> Workspace 负责回答“当前工作区里真实存在什么、它们在哪里、以及上层该如何安全访问它们”。

## 目标类图

`Workspace` 的目标类图已拆分到单独文档：

- `src/Dna.Knowledge/Workspace/CLASS-DIAGRAM.md`

该文档只负责描述 `Workspace` 作为 `TechnicalNode` 的目标模块模型，不承担完整架构说明。

## 模块职责

当前 `Workspace` 的职责包括：

- 扫描工作区目录和文件
- 输出稳定的目录快照与条目事实
- 通过 `FileSystemWatcher` 监听变化并发布失效/变更事件
- 提供受工作区根目录约束的安全路径映射
- 提供基础文本与二进制文件读写
- 维护每个目录下的 `.agentic.meta` 元数据文件
- 在目录树范围内批量补齐 `.agentic.meta`

## 非职责范围

`Workspace` 不负责：

- 记忆 CRUD
- 知识图谱父子层级治理
- 知识依赖关系推理
- 记忆向知识的演化判定
- Agent 工作流编排

## 依赖方向

```text
Dna.Knowledge.Workspace
    ->
Dna.Core
```

`Workspace` 不允许反向依赖这些上层模块：

- `Dna.Knowledge.Memory`
- `Dna.Knowledge.TopoGraph`
- `Dna.Knowledge.Evolution`

## 核心设计

### 文件系统优先

真实文件系统是事实源。
`Workspace` 不应该再发明一套脱离磁盘的虚拟项目树来替代它。

这里的核心原则是：

> 直接使用真实文件系统，再在其上封装工作区级索引、目录元数据和安全 IO。

这意味着：

- 上层通过 `WorkspaceEngine` 消费工作区事实
- 底层真实来源依然是磁盘上的目录和文件
- `Workspace` 是包装层，不是替代层

这里还需要明确一条长期原则：

> 理想状态下，一个模块应尽量映射一个主物理目录；但系统设计不能假设模块与物理目录永远严格一一对应。

这意味着：

- 能做到一模块一主目录时，应优先保持这种结构
- 真实项目中允许一个模块映射多个物理路径
- 也允许一个目录中存在尚未被清晰拆分的混合内容
- `Workspace` 只记录物理事实，不强行把目录等同于模块
- 模块投影、父子层级和关系解释应交给上层知识图模块

### `.agentic.meta`

每个目录都可以包含一个 `.agentic.meta` 文件，内容使用 JSON 格式。
它是目录的侧车元数据文件，语义上类似 Unity 的 `.meta`。

它用于描述目录级知识与身份信息，包括：

- 文档结构版本
- 稳定 GUID
- 目录摘要
- 最近更新时间

这个设计解决的是：

- 目录结构可以辅助推断层级，但目录结构本身不一定就是最终模块结构
- 程序、美术、策划、工具等目录在真实项目中可能交叉放置
- 所以目录本身也需要一个稳定元数据文件，不能只依赖目录路径

当前已支持：

- 读取 `.agentic.meta`
- 为单个目录自动创建默认 `.agentic.meta`
- 写回 `.agentic.meta`
- 递归为整个目录树补齐 `.agentic.meta`
- 扫描时把 `.agentic.meta` 视为目录元数据，而不是普通文件节点

### WorkspaceDirectorySnapshot

表示一次对某个目录的扫描结果，包含：

- 工作区根路径
- 当前相对路径
- 当前完整路径
- 是否存在
- 扫描时间
- 直接子目录数量
- 直接子文件数量
- 直接子条目列表

### WorkspaceFileNode

表示一个真实工作区条目，可以是目录，也可以是文件。

它同时携带两类信息：

- 文件系统事实
  - `Name`
  - `Path`
  - `ParentPath`
  - `FullPath`
  - `Kind`
  - `Extension`
  - `SizeBytes`
  - `LastModifiedUtc`
  - `HasChildren`
- 工作区解释层事实
  - `Ownership`
  - `Module`
  - `Descriptor`
  - `Status`
  - `Actions`

### WorkspaceChangeSet

表示一次工作区变更通知批次，供上层订阅：

- 变更时间
- 变更条目列表
- 变更类型
- 目标类型
- 当前路径
- 父路径
- 重命名时的旧路径

## 对外接口

当前 `IWorkspaceEngine` 对外提供三类能力。

### 1. 工作区事实查询

- `GetRootSnapshot(...)`
- `GetDirectorySnapshot(...)`
- `TryGetEntry(...)`
- `GetRoots(...)`
- `GetChildren(...)`

### 2. 路径与文件 IO

- `ResolveFullPath(...)`
- `ResolveMetadataFilePath(...)`
- `ReadTextAsync(...)`
- `ReadBytesAsync(...)`
- `WriteTextAsync(...)`
- `WriteBytesAsync(...)`
- `TryReadDirectoryMetadata(...)`
- `EnsureDirectoryMetadataAsync(...)`
- `WriteDirectoryMetadataAsync(...)`
- `EnsureDirectoryMetadataTreeAsync(...)`
- `EnsureDirectory(...)`
- `DeleteEntry(...)`

### 3. 实时变更通知

- `Changed`
- `Invalidate(...)`
- `InvalidateAll()`

## Workspace 模块能力清单

这一节只属于 `Workspace` 模块本身。
它记录当前 `Dna.Knowledge.Workspace` 已实现的能力、底层支撑能力、数据语义和当前边界。

### 对外能力

`Workspace` 当前已经提供这些面向上层的能力：

- 基于 `projectRoot + ArchitectureManifest` 的工作区初始化
- 根目录快照查询
- 任意目录快照查询
- 按相对路径查询单个条目
- 根节点列表查询
- 子节点列表查询
- 安全文本读写
- 安全二进制读写
- 安全创建目录
- 安全删除文件或目录
- 读取目录的 `.agentic.meta`
- 确保目录存在 `.agentic.meta`
- 写回目录的 `.agentic.meta`
- 递归为目录树补齐 `.agentic.meta`
- 订阅工作区变更事件
- 手动失效单一路径缓存
- 手动全量重置缓存

### 底层支撑能力

`Workspace` 当前已经具备这些底层能力：

- 以真实文件系统为事实源的工作区模型
- 相对路径归一化与根目录边界校验
- 基于 `WorkspaceDirectorySnapshot` 的目录快照构建
- 基于 `WorkspaceFileNode` 的条目结构化表达
- 面向异常场景的 best-effort 扫描
- 基于 `ModulesManifest` 的模块归属识别
- 基于 `ManagedPaths` 的托管范围识别
- `.agentic.meta` 的读取、归一化和描述投影
- 在普通文件列表中隐藏 `.agentic.meta`
- 支持架构层扩展的默认排除目录规则
- 目录快照缓存
- 基于 `FileSystemWatcher` 的文件系统变化监听
- 变更事件的防抖与批量发布
- 返回克隆快照，避免调用方污染缓存

### 当前数据语义

当前模块已经明确建模了这些概念：

- 工作区条目类型
  - `Directory`
  - `File`
- 工作区条目状态
  - `Registered`
  - `CrossWork`
  - `Described`
  - `Managed`
  - `Tracked`
  - `Container`
  - `Candidate`
  - `Untracked`
- 目录元数据字段
  - `Schema`
  - `StableGuid`
  - `Summary`
  - `UpdatedAtUtc`
- 工作区变更类型
  - `Created`
  - `Changed`
  - `Deleted`
  - `Renamed`
  - `Invalidated`
  - `Reset`

### 当前边界

当前模块还没有提供这些能力：

- 全文索引
- 文件指纹或哈希索引
- 内容级 diff 感知
- 事务式写入批处理
- 冲突合并
- 图谱投影规则
- 记忆存储
- 知识治理

这意味着：

> Workspace 现在已经是一个可用的工作区事实引擎，但还不是内容索引引擎，也不是知识图谱引擎。

## 安全边界

`Workspace` 的所有读写入口都必须遵守一条硬性规则：

> 任何路径都不能逃逸出当前工作区根目录。

当前通过 `WorkspacePath.ResolveFullPathWithinRoot(...)` 统一做路径归一化与越界校验。

这意味着：

- 上层可以使用相对路径请求读写
- 但不能借助 `../` 等方式访问工作区外内容

## 扫描与缓存实现

### WorkspaceScanner

职责包括：

- 扫描一个目录的直接子目录和直接子文件
- 生成 `WorkspaceDirectorySnapshot`
- 根据模块根路径和 managed path 标注条目归属
- 读取 `.agentic.meta` 并为目录生成描述信息
- 在普通文件列表中隐藏 `.agentic.meta`

默认排除项包括：

- `.agentic-os`
- `.project.dna`
- `.git`
- `bin`
- `obj`
- 其他默认工具与构建目录
- `ArchitectureManifest.ExcludeDirs` 中追加的目录

### WorkspaceTreeCache

职责包括：

- 缓存目录快照
- 文件系统变化后失效当前目录与父目录缓存
- 目录变化时级联清理后代缓存
- 批量发布 `WorkspaceChangeSet`
- 对外返回克隆后的快照，避免外部污染缓存

## 与上层模块的关系

- `Workspace`
  - 回答真实工作区里有什么、在哪里、能否安全访问
- `TopoGraph`
  - 回答这些模块如何组织成层级和关系
- `Memory`
  - 回答哪些事实和交互被记忆下来
- `Evolution`
  - 回答哪些记忆被压缩、沉淀并升级为稳定知识

## 当前重构结论

当前 `Workspace` 已经从“目录树扫描辅助器”收口为真正的工作区底座：

- 不再只关心目录，也正式纳入文件事实
- 不再只有查询，也支持监听和失效
- 不再只是被动扫描，也提供基础安全 IO
- 不再把模块身份完全绑定路径，也支持目录级 `.agentic.meta`

后续上层模块应优先围绕这套工作区事实接口消费 `Workspace`，
而不是绕过它直接访问磁盘。
