# Dna.Agent

> 状态：第一阶段架构基线
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Agent`

> 当前说明：`Dna.Agent` 的深化开发暂时暂停，等待 `Dna.Workbench` 的 `resolveRequirement / startTask / endTask` 语义先稳定。

## 模块定位

`Dna.Agent` 是位于 `App` 与 `Dna.Workbench` 之间的 Agent Runtime 模块。

它负责内置 Agent 的：

- 需求规划
- 任务编排
- 执行循环
- 模型调用
- 工具调用调度
- 会话生命周期管理

一句话概括：

> `Dna.Agent` 负责让内置 Agent 会思考、会拆解、会执行；`Dna.Workbench` 负责让它每次只能在一个正确的模块上下文里工作。

## 分层位置

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
```

其中：

- `App`
  - 提供桌面 UI、CLI、MCP、HTTP 等宿主与适配层
- `Dna.Agent`
  - 负责内置 Agent 的编排与执行
- `Dna.Workbench`
  - 负责项目能力供给、需求拆解支持、任务上下文供给、模块锁与任务闭环
- `Dna.Knowledge`
  - 负责知识域实现

## 设计原则

### 1. 编排与供能分离

- `Dna.Agent`
  - 决定需求如何拆解、任务如何推进、何时调用工具、何时结束任务
- `Dna.Workbench`
  - 提供工作区、拓扑、知识、记忆、任务上下文、模块锁、运行时观测等能力

### 2. 内置 Agent 与外置 Agent 不混层

外置 Agent（Cursor、Codex、Claude Code 等）本身已经具备自己的编排能力。  
它们不需要依赖 `Dna.Agent` 才能工作，只需要通过 MCP / CLI / HTTP 适配层使用 `Dna.Workbench` 的项目能力。

因此：

- `Dna.Agent`
  - 面向“内置 Agent Runtime”
- `Dna.Workbench`
  - 面向“所有 Agent 的统一项目能力面”

### 3. 统一运行时观测

无论运行主体是：

- 内置 Agent
- 外置 Agent
- 人工触发流程

都应尽可能产出统一运行时事件，并上报到 `Dna.Workbench`，让拓扑图实时显示工作链路。

## 内置 Agent 的标准流程

内置 Agent 在最终架构中的标准流程应为：

1. 向 `Workbench` 请求需求拆解
2. 根据拆解结果生成多个单模块 task
3. 为每个 task 调用 `startTask`
4. 基于返回的模块知识、相关记忆与 workspace 边界执行任务
5. 必须调用 `endTask` 回写结果、决策、教训与阻塞项
6. 再决定剩余 task 串行还是并行推进

其中：

- 每个 task 只能绑定一个目标模块
- 并行 task 不能冲突占用同一个模块
- 如需继续需求，必须重新发起新的 task

这里的关键边界是：

- Agent 负责决定“哪些 task 并行”
- Workbench 只负责用模块锁拒绝非法并行

## 内置 Agent 的治理流程

除普通任务外，内置 Agent 还应支持治理流程：

1. 向 `Workbench` 发起治理请求
2. 获取全局或指定模块范围的治理模块树上下文
3. 根据治理树拆解出一批治理型单模块 task
4. 串行或并行调用 `startTask`
5. 在治理上下文内完成记忆压缩、知识沉淀或知识修正
6. 通过 `endTask` 回写治理结果、决策、教训与后续建议

治理 task 与普通 task 的共同约束不变：

- 一个 task 只能绑定一个目标模块
- 同模块禁止并行占用
- 后续继续治理仍需重新发起新的 task

## 核心职责

`Dna.Agent` 负责：

- 管理任务会话与生命周期
- 管理 Ask / Plan / Agent 等执行模式
- 根据 Workbench 的拆解结果构建执行计划
- 决定 task 的串行与并行顺序
- 负责模型调用与流式输出
- 负责工具调用流程编排
- 负责把运行过程事件上报到 `Dna.Workbench`

## 非职责范围

`Dna.Agent` 不负责：

- 直接扫描工作区
- 直接维护模块拓扑
- 直接存储知识与记忆
- 直接生成任务上下文
- 直接实现 MCP / CLI / HTTP 协议
- 直接替代桌面 UI

这些职责分别属于：

- `Dna.Workbench`
- `Dna.Knowledge`
- `App`

## 计划中的核心接口

第一阶段建议围绕以下接口展开：

- `IAgentRuntimeService`
  - 启动、恢复、取消任务会话
- `IAgentPlanner`
  - 消费需求拆解结果并生成计划
- `IAgentExecutor`
  - 驱动执行循环并协调 `startTask / endTask`
- `IToolCallCoordinator`
  - 管理工具调用与返回结果
- `IAgentModelGateway`
  - 统一模型调用
- `IAgentSessionStore`
  - 持久化会话与中间状态

## 与现有代码的迁移关系

当前以下内容是 `Dna.Agent` 的直接候选迁移源：

- `src/Dna.Workbench/Agent/*`
- `src/Dna.Workbench/Models/Agent/*`
- 与 Agent Pipeline 相关的过渡实现

迁移原则：

1. 先迁移编排职责，再稳定新接口
2. 不把知识能力反向迁入 `Dna.Agent`
3. 所有知识、记忆、工作区访问统一通过 `Dna.Workbench`
4. 不在 `Dna.Agent` 内直接绕过 Workbench 生成任务上下文

## 当前结论

`Dna.Agent` 的存在意义不是新增一层复杂度，而是把下面两件事彻底拆开：

- Agent 如何规划、拆解与执行
- 项目知识能力如何统一提供与限域

这样后续无论内置 Agent 演进到多强，都不会把 `Dna.Workbench` 再次拖回“又懂知识又懂编排”的混合状态。
