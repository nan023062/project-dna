# Dna.Workbench

> 状态：当前有效（按 2026-04-04 架构决策收口）
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Workbench`

## 模块定位

`Dna.Workbench` 是位于 `App` / `Dna.Agent` 与 `Dna.Knowledge` 之间的应用服务层模块。

它不是新的知识引擎，也不是 Agent 编排器。  
它的职责是把 `Workspace / TopoGraph / Memory / Governance` 这些底层能力整理成稳定、统一、可复用的工作台能力，供内置 Agent、外置 Agent、CLI 与桌面 UI 共用。

一句话概括：

> `Dna.Workbench` 是 Agentic OS 的能力中台，不负责替 Agent 思考，只负责把项目能力稳定地提供给 Agent 和宿主使用。

## 分层位置

### 当前实现层级

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

### 核心职责分工

- `App`
  - 负责桌面宿主、UI、CLI / MCP / HTTP 适配
- `Dna.Agent`
  - 负责内置 Agent 的任务编排、执行循环、模型调用、工具调用策略与会话生命周期
- `Dna.Workbench`
  - 负责统一项目能力入口、知识用例封装、运行时观测入口
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

它们都不该直接操作底层知识实现细节，也不该各自维护一套“如何读工作区、如何查模块、如何写记忆”的逻辑。

它们真正需要的是一套统一的项目能力面，包括：

- 工作区目录与文件语义
- 模块拓扑与关系查询
- 模块知识读写
- 记忆读写与召回
- 统一的运行时状态上报与拓扑投影
- 后续统一的工具能力注册与调用入口

这些能力应由 `Dna.Workbench` 统一提供。

## 核心职责

`Dna.Workbench` 负责：

- 为桌面 UI、内置 Agent、外置 Agent、CLI 提供统一的项目能力入口
- 组合 `Dna.Knowledge` 的领域能力，形成可直接消费的应用用例
- 封装“查工作区、查拓扑、查模块知识、写记忆、召回记忆”等高层能力
- 提供统一的运行时观测入口，让任意 Agent 都能把运行事件投影到拓扑图
- 为后续工具调用内核预留稳定的能力边界

## 非职责范围

`Dna.Workbench` 不负责：

- 任务规划
- 步骤拆解
- 多轮执行循环
- 大模型会话管理
- 工具选择策略
- 自动重试与恢复策略
- 对话策略与提示词编排

这些职责属于 `Dna.Agent`，或属于外置 Agent 自身。

## 当前稳定能力面

当前 `Dna.Workbench` 已收口为三大能力面：

- `IKnowledgeWorkbenchService`
  - 提供工作区、拓扑、模块知识、记忆相关能力
- `IWorkbenchToolService`
  - 提供统一工具目录、工具调用入口与后续内外 Agent 共用的能力语义
- `IWorkbenchRuntimeService`
  - 提供统一运行时事件写入与拓扑运行态读取能力

而 `IWorkbenchFacade` 当前只聚合：

- `Knowledge`
- `Tools`
- `Runtime`

这意味着 `Workbench` 已经不再直接聚合 Agent 编排接口。

## 设计原则

### 1. Agent 负责编排，Workbench 负责供能

无论 Agent 是内置还是外置：

- Agent 决定“下一步做什么”
- Workbench 决定“在这个项目里能做什么”

### 2. 内外能力面一致

内置 Agent 与外置 Agent 不应看到两套不同的项目能力。

理想状态下，二者都通过同一套 Workbench 能力访问：

- Workspace
- TopoGraph
- Memory
- Governance
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

当前 `Dna.Workbench` 主要包含这些子域：

- `Contracts`
  - 对外稳定接口
- `Knowledge`
  - 知识、工作区、记忆等高层能力封装
- `Runtime`
  - 运行时事件接收、事件流管理、拓扑投影
- `Tooling`
  - 后续统一工具能力注册与调用入口

## 当前过渡实现说明

当前代码里仍有一部分历史遗留内容尚未完全迁干净：

- `src/Dna.Workbench/Agent/Pipeline/*`

这部分更接近早期内置 Agent / 执行管线试验代码，已经不再是 `Dna.Workbench` 的长期边界。  
后续要么迁到 `Dna.Agent`，要么按新的 Agent Runtime 方案重构后替换。

约束如下：

- 不再把新的任务编排职责继续加进 `Dna.Workbench`
- 如需继续补 Workbench，只补“能力接口”与“运行时观测”
- 历史管线代码不再作为新增功能的落点

## 典型调用链

### 桌面 UI

```text
Desktop UI
  ->
Workbench
  ->
Dna.Knowledge
```

桌面内部优先走进程内调用，不应长期依赖本地 HTTP 作为内部主路径。

### 外置 Agent / CLI / MCP

```text
External Agent / CLI / MCP
  ->
Adapter
  ->
Workbench
  ->
Dna.Knowledge
```

适配层只负责协议、参数与输出格式，不负责真正的业务编排。

### 内置 Agent

```text
Built-in Agent Runtime
  ->
Dna.Agent
  ->
Workbench
  ->
Dna.Knowledge
```

内置 Agent 负责规划与执行，Workbench 负责能力供给。

## 当前结论

`Dna.Workbench` 的正确定位不是“Agent 编排层”，而是：

> 一个位于 `Dna.Knowledge` 之上的能力中台模块，负责把项目知识能力整理成统一、稳定、可复用的工作台能力，并同时服务内置 Agent、外置 Agent、CLI 与桌面宿主。
