# Dna.Workbench

> 状态：当前有效（按 2026-04-04 架构决策收口）
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Workbench`

## 模块定位

`Dna.Workbench` 是位于 `App` / `Dna.Agent` / `Dna.ExternalAgent` 与 `Dna.Knowledge` 之间的应用服务层模块。

它不是新的知识引擎，也不是 Agent 编排器。  
它的职责是把 `Workspace / TopoGraph / Memory / Governance` 这些底层能力整理成稳定、统一、可复用的知识桥接能力，供内置 Agent、外置 Agent、CLI 与桌面 UI 共用。

一句话概括：

> `Dna.Workbench` 是 Agentic OS 的任务桥接层、知识桥接层与治理桥接层，不负责替 Agent 思考，也不负责任务编排；它只负责把需求或治理意图收口到受限任务，并为每个任务提供唯一目标模块或治理范围、唯一操作空间、完整上下文与模块锁保护。

## 分层位置

### 目标层级

```text
App
  ->
Dna.Agent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core

App
  ->
Dna.ExternalAgent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

### 核心职责分工

- `App`
  - 负责桌面宿主、UI、CLI / MCP / HTTP 适配
- `Dna.Agent`
  - 负责内置 Agent 的任务编排、执行循环、模型调用、工具调用策略与会话生命周期
- `Dna.ExternalAgent`
  - 负责外置 Agent 产品适配与任务循环约束注入
- `Dna.Workbench`
  - 负责需求拆解支持、治理范围解析、任务桥接封装、上下文供给、任务互斥与运行时观测
- `Dna.Knowledge`
  - 负责知识域能力本身
- `Dna.Core`
  - 负责底层基础设施

## 为什么需要这个模块

无论是：

- 桌面内置 Chat
- 未来自带的本地 Agent Runtime
- Cursor / Codex / Claude Code 这类外置 Agent
- 本地 CLI

它们都不该直接操作底层知识实现细节，也不该各自维护一套“如何读工作区、如何拆需求、如何查模块、如何写记忆”的逻辑。

它们真正需要的是一套统一的项目能力面，包括：

- 工作区目录与文件语义
- 模块拓扑与关系查询
- 基于 `TopoGraph + MCDP` 的需求拆解支持
- 面向记忆与知识演化的治理范围解析
- 模块知识读写
- 记忆读写与召回
- 基于 workspace 元信息的封闭操作空间
- 单任务上下文与单任务结果闭环
- 统一的运行时状态上报与拓扑投影
- 后续统一的工具能力注册与调用入口

这些能力应由 `Dna.Workbench` 统一提供。

## 核心职责

`Dna.Workbench` 负责：

- 为桌面 UI、内置 Agent、外置 Agent、CLI 提供统一的项目能力入口
- 组合 `Dna.Knowledge` 的领域能力，形成可直接消费的应用用例
- 基于 `TopoGraph + MCDP` 辅助 Agent 拆解需求涉及的模块、父子层级、依赖链与协作链
- 基于治理意图返回全局或指定模块的治理模块树上下文
- 为每个单任务提供唯一目标模块、唯一可操作空间与完整上下文
- 为每个活动 task 提供模块锁，防止多个 Agent 同时修改同一目标模块
- 用 workspace 元信息为任务提供封闭可操作边界
- 把精准隔离的模块知识与相关记忆作为任务上下文返回给 Agent
- 在任务结束时记录执行结果、关键决策、经验教训、失败原因与前置依赖
- 根据任务结果决定如何写入短期记忆、长期记忆与治理链路
- 提供统一的运行时观测入口，让任意 Agent 都能把运行事件投影到拓扑图
- 为后续统一工具调用内核提供稳定的能力边界

## 非职责范围

`Dna.Workbench` 不负责：

- 任务调度
- 任务规划
- 步骤推进
- 多轮执行循环
- 大模型会话管理
- 工具选择策略
- 自动重试与恢复策略
- 对话策略与提示词编排

这些职责属于 `Dna.Agent`，或属于外置 Agent 自身。

换句话说：

- `Workbench` 不是调度器
- `Workbench` 不是编排器
- `Workbench` 是带模块锁的任务封装层

## 最核心概念：需求拆解 + 单任务封装

`Dna.Workbench` 的最核心抽象不是“长会话编排”，而是：

1. 先辅助 Agent 做模块级需求拆解
2. 或先返回治理范围对应的模块树上下文
3. 再对每个模块级任务提供严格受限的 task context

### 标准闭环

#### 1. 需求拆解

Agent 先请求 `Workbench` 基于 `TopoGraph + MCDP` 解析当前需求涉及的：

- 主目标模块
- 依赖链模块
- 协作链模块
- 父子层级导航

这一步的产物不是直接执行，而是一组可供 Agent 选择和编排的模块级任务候选。

### 治理闭环

除正常需求闭环外，`Workbench` 还要承接一条“记忆和知识治理进化闭环”。

#### 1. 发起治理请求

Agent 可以向 `Workbench` 发起治理任务，请求可以是：

- 全局治理
- 指定模块治理
- 某个模块子树治理

`Workbench` 需要返回与这次治理范围对应的模块树上下文，而不是直接返回单个执行 task。

这个治理上下文至少应包括：

- 治理范围内的模块树
- 每个模块的知识状态摘要
- 相关长期记忆与待沉淀记忆线索
- 依赖链与协作链上的治理关联
- 建议的治理边界

#### 2. Agent 自己拆解治理顺序

Agent 根据治理模块树上下文，自行拆解治理顺序，形成一批治理型 `single agent session task`。

这些治理 task 仍然遵守：

- 一个 task 只绑定一个目标模块
- 每个 task 都要有明确治理目标
- 可以有前置依赖

#### 3. 依次或并行执行治理任务

Agent 再根据自己的编排能力，串行或并行调用 `Workbench.startTask(...)` 启动这些治理 task。

治理 task 与普通 task 共用同一套生命周期：

- `startTask`
- 获取精准上下文
- 在受限空间内执行
- `endTask`

但治理 task 的上下文更偏向：

- 模块知识现状
- 相关记忆
- 待压缩与待演化内容
- 模块树中的相对位置

#### 4. `endTask` 回写治理结果

治理 task 结束后，Agent 仍然必须调用 `Workbench.endTask(...)`，并携带：

- 治理结果
- 决策说明
- 经验教训
- 未完成原因
- 后续治理建议

`Workbench` 再根据结果决定如何：

- 更新记忆状态
- 推动知识沉淀
- 补充治理记录
- 释放该模块 task 租约

#### 5. 循环直到治理需求完成

后续治理 task 继续按照普通任务相同的方式依次推进，直到整体治理需求完成。

#### 2. Agent 自己创建多个单任务

Agent 根据拆解结果，自行创建多个 `single agent session task`。

每个 task 只能对应：

- 一个目标模块
- 一个作业目标
- 一个成功标准

并可携带：

- 前置 task 依赖
- 协作说明
- 期望结果

#### 3. `startTask`

Agent 调用 `Workbench.startTask(...)` 后，`Workbench` 必须：

- 校验目标模块是否已被其他活动 task 占用
- 校验前置依赖是否满足
- 为当前 task 分配唯一模块租约
- 对目标模块加锁
- 返回与该 task 绑定的完整上下文

这个上下文至少包括：

- 模块知识
- 相关记忆
- workspace 操作边界
- 依赖与协作关系
- 约束条件

同时，`Workbench` 必须把这个 task 与当前 agent session 绑定，直到收到结束通知。

#### 4. `endTask`

无论任务结果是成功、失败还是阻塞，Agent 都必须调用 `Workbench.endTask(...)`。

`endTask` 至少要携带：

- 任务结果
- 关键决策
- 教训与经验
- 失败原因
- 前置依赖或阻塞项

`Workbench` 再根据结果：

- 回写记忆
- 记录任务摘要
- 释放模块租约
- 释放模块锁
- 更新治理链路所需的输入

#### 5. 推进剩余任务链

Agent 再根据前一轮 task result 决定后续任务如何推进：

- 串行 `startTask`
- 并行 `startTask`

但并行时必须满足：

- 不同 task 对应不同目标模块
- 同一模块不允许被多个活动 task 同时占用

### 最重要的硬约束

- 一个 task 只能绑定一个目标模块
- 一个 task 只能获得一个封闭操作空间
- 一个活动 task 必须持有该模块的唯一模块锁
- Agent 必须先拆解需求，再按模块创建 task
- Agent 必须先 `startTask`，再执行
- Agent 必须调用 `endTask`
- 如需继续需求，必须重新发起新的 task
- `Workbench` 允许并行 task，但目标模块必须严格互斥

## 模块锁的价值

模块锁不是编排功能，而是并发保护功能。

它的核心目的不是“帮 Agent 排顺序”，而是：

- 防止多个 Agent 同时修改同一个目标模块
- 防止多个任务同时写入同一工作区边界
- 降低并行开发时的冲突与合并风险
- 把冲突从“事后 merge”前移到“task 启动时即拒绝”

## 最终稳定能力面

按最终架构收口，`Dna.Workbench` 应具备四大能力面：

- `IKnowledgeWorkbenchService`
  - 提供工作区、拓扑、模块知识、记忆相关能力
- `IWorkbenchGovernanceService`
  - 提供治理范围解析、治理模块树上下文与记忆/知识演化入口
- `IWorkbenchTaskService`
  - 提供需求拆解、任务启动、任务结束、任务冲突检测与会话绑定能力
- `IWorkbenchToolService`
  - 提供统一工具目录、工具调用入口与后续内外 Agent 共用的能力语义
- `IWorkbenchRuntimeService`
  - 提供统一运行时事件写入与拓扑运行态读取能力

而 `IWorkbenchFacade` 应统一聚合：

- `Knowledge`
- `Governance`
- `Tasks`
- `Tools`
- `Runtime`

## 设计原则

### 1. Agent 负责编排，Workbench 负责供能与限域

无论 Agent 是内置还是外置：

- Agent 决定“需求怎么拆、下一步做什么”
- Workbench 决定“这次任务能访问什么、能操作哪里、什么时候必须结束”

### 2. 内外能力面一致

内置 Agent 与外置 Agent 不应看到两套不同的项目能力。

理想状态下，二者都通过同一套 Workbench 能力访问：

- Workspace
- TopoGraph
- Memory
- Governance
- Governance Context
- Tasks
- Tools
- Runtime

区别只在于：

- 内置 Agent 通过进程内调用接入
- 外置 Agent 通过 MCP / CLI / HTTP 适配层接入

### 3. UI 不承载应用编排

桌面 UI 负责展示与交互，不应自己拼装知识域调用链。  
真正的用例组织应该沉到 Workbench。

### 4. 运行时观测与任务编排解耦

拓扑图上的实时运行状态，不依赖某一个具体 Agent 实现。  
任何 Agent 只要能上报统一运行时事件，Workbench 就能投影出统一的运行态视图。

## 内部子域

当前 `Dna.Workbench` 的长期子域应收口为：

- `Contracts`
  - 对外稳定接口
- `Knowledge`
  - 知识、工作区、记忆等高层能力封装
- `Governance`
  - 治理范围解析、治理模块树、知识演化输入输出
- `Tasks`
  - 需求拆解、任务上下文、任务租约、任务闭环
- `Runtime`
  - 运行时事件接收、事件流管理、拓扑投影
- `Tooling`
  - 统一工具能力注册与调用入口

## 当前过渡实现说明

当前代码里仍有一部分历史遗留内容尚未完全迁干净：

- `src/Dna.Workbench/Agent/Pipeline/*`

这部分更接近早期内置 Agent / 执行管线试验代码，已经不再是 `Dna.Workbench` 的长期边界。  
后续要么迁到 `Dna.Agent`，要么按新的 Agent Runtime 方案重构后替换。

约束如下：

- 不再把新的任务编排职责继续加进 `Dna.Workbench`
- 如需继续补 Workbench，只补“知识桥接 / 任务桥接 / 工具能力 / 运行时观测”
- 治理相关能力优先落在独立的 `Governance` 子域，而不是塞回 `Tasks`
- 历史管线代码不再作为新增功能的落点

## 典型调用链

### 外置 Agent

```text
External Agent
  ->
Workbench.resolveRequirement(...)
  ->
Agent creates module tasks
  ->
Workbench.startTask(...)
  ->
Dna.Knowledge
  ->
Workbench.endTask(...)
```

### 内置 Agent

```text
Built-in Agent Runtime
  ->
Dna.Agent
  ->
Workbench.resolveRequirement(...)
  ->
Dna.Agent creates module tasks
  ->
Workbench.startTask(...)
  ->
Dna.Knowledge
  ->
Workbench.endTask(...)
```

### 桌面 UI

```text
Desktop UI
  ->
Workbench
  ->
Dna.Knowledge
```

桌面内部优先走进程内调用，不应长期依赖本地 HTTP 作为内部主路径。

## 当前结论

`Dna.Workbench` 的正确定位不是“Agent 编排层”，而是：

> 一个位于 `Dna.Knowledge` 之上的任务桥接层与知识桥接层模块，负责基于 `TopoGraph + MCDP` 帮助 Agent 拆解需求，把每个任务约束到唯一模块与唯一操作空间内，并提供统一的任务上下文、工具能力、运行时观测与结果回写闭环。
>
> 同时，它还负责面向记忆与知识演化的治理桥接：先返回治理范围对应的模块树上下文，再让 Agent 把治理拆成多个单模块 task，最终通过相同的 `startTask / endTask` 生命周期完成治理闭环。
