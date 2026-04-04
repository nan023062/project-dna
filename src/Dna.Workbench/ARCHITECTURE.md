# Dna.Workbench

> 状态：第一阶段架构基线
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Workbench`

## 模块定位

`Dna.Workbench` 是位于 `App` 与 `Dna.Knowledge` 之间的应用层模块。

它不是新的知识引擎，也不是新的桌面 UI 层。
它的职责是把底层知识能力组织成真正可被桌面端、CLI、MCP 和未来外部 Agent 复用的应用服务。

一句话概括：

> `Dna.Workbench` 是 Agentic OS 的应用编排层，负责 Agent 任务编排、知识用例编排、运行时事件与拓扑投影。

## 分层位置

目标分层如下：

```text
App
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

其中：

- `App`
  - 负责桌面宿主、HTTP / MCP / CLI 适配、Avalonia UI
- `Dna.Workbench`
  - 负责应用级编排与运行时
- `Dna.Knowledge`
  - 负责知识域能力
- `Dna.Core`
  - 负责底层基础设施

## 为什么需要这个模块

当前仓库里已经存在以下趋势：

- `App` 中有本地 API / CLI / MCP 适配层
- `App.Services.Pipeline` 中已经出现了初步的 Agent 编排代码
- 桌面端后续会支持内置 Agent 编排
- 拓扑图后续要实时显示 Agent 在各知识节点之间的进入、查询、协作和工作链路
- 同一套运行时能力还要服务外部 Agent

如果继续把这些逻辑堆在 `App` 里，会导致：

- 宿主层与应用层混在一起
- 桌面 UI、HTTP、MCP、CLI 都各自编排知识域对象
- 后续很难把“内部桌面直连”和“外部协议适配”区分清楚

因此需要新增一个单独模块，把“应用服务”和“Agent 运行时”正式提炼出来。

## 模块职责

`Dna.Workbench` 负责：

- 统一桌面端与外部 Agent 的应用服务入口
- 编排 `Workspace / TopoGraph / Memory / Governance` 的组合用例
- 提供知识查询、知识写入、记忆读写等高层能力
- 提供任务会话、步骤推进、执行状态等 Agent 运行时能力
- 发布统一运行时事件
- 将运行时事件投影为拓扑图所需的实时状态

## 非职责范围

`Dna.Workbench` 不负责：

- 直接渲染桌面 UI
- 直接承载 HTTP 协议细节
- 直接定义 MCP 工具协议
- 直接处理 CLI 参数解析
- 直接实现文件扫描、记忆存储或图谱存储

这些能力分别属于：

- `App`
  - 宿主与适配层
- `Dna.Knowledge`
  - 底层知识域引擎

## 内部子域

第一阶段将 `Dna.Workbench` 内部划分为 4 个子域：

- `Contracts`
  - 对外门面与应用层接口
- `Knowledge`
  - 高层知识用例编排
- `Agent`
  - 任务会话、步骤、编排、执行状态
- `Runtime`
  - 事件总线与拓扑运行时投影

当前阶段先建立接口与模型基线，不急于一次性完成实现。

## 第一批稳定接口

当前先定 5 个关键接口，作为后续实现基线：

- `IWorkbenchFacade`
  - 总门面
  - 聚合知识服务与 Agent 服务
- `IKnowledgeWorkbenchService`
  - 面向桌面端和外部适配层的高层知识入口
- `IAgentOrchestrationService`
  - 负责任务启动、取消、会话状态读取
- `IAgentRuntimeEventBus`
  - 负责运行时事件发布与订阅
- `ITopologyRuntimeProjectionService`
  - 负责把运行时事件投影成拓扑图实时视图

## 目标调用方式

后续目标是把当前“桌面内部也走本地 HTTP”的模式，逐步收敛为：

```text
Desktop UI
  ->
Workbench Facade / Application Service
  ->
Dna.Knowledge

CLI / MCP / HTTP
  ->
Adapter
  ->
Workbench Facade / Application Service
  ->
Dna.Knowledge
```

也就是：

- 桌面内部直接调用 `Dna.Workbench`
- 外部入口通过适配层调用 `Dna.Workbench`
- 双方共用同一套应用层

## 运行时事件原则

`Dna.Workbench` 还要承担未来的 Agent 运行时观测职责。

后续事件流至少需要覆盖：

- `TaskStarted`
- `TaskCompleted`
- `TaskFailed`
- `NodeEntered`
- `NodeExited`
- `KnowledgeQueried`
- `KnowledgeUpdated`
- `MemoryRead`
- `MemoryWritten`
- `ToolInvoked`
- `RelationTraversed`
- `CollaborationTriggered`

拓扑图不应区分“这是桌面内置 Agent 还是外部 Agent”，而应统一消费这套运行时事件。

## 与现有代码的迁移关系

当前迁移方向如下：

1. `App.Services.Pipeline`
   - 后续迁入 `Dna.Workbench.Agent`
2. `App.Interfaces.Api`
   - 后续只保留 HTTP 适配，不继续承载应用编排
3. `App.Interfaces.Mcp`
   - 后续只保留 MCP 工具适配，业务逻辑收口到 `Dna.Workbench`
4. `App.Interfaces.Cli`
   - 后续只保留命令行解析与结果输出
5. 桌面 ViewModel
   - 后续逐步改为直接依赖 `Dna.Workbench` 对外接口

## 当前阶段结论

当前 `Dna.Workbench` 的第一阶段目标不是“立刻重写全部逻辑”，而是：

- 先把应用层边界定清楚
- 先把接口命名定稳
- 先把架构文档和类图立起来
- 为后续迁移提供明确落点

只有这一步完成后，后面的 Agent Runtime、拓扑实时投影和桌面直连应用服务，才不会反复返工。
