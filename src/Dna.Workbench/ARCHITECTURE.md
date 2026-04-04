# Dna.Workbench 架构设计与实现指南

> 状态：Active
> 最后更新：2026-04-04
> 适用范围：`src/Dna.Workbench`

## 1. 模块定位

`Dna.Workbench` 是 Agentic OS 的**任务桥接层、知识桥接层与治理桥接层**。
它相当于 Agentic OS 的“内核（Kernel）”，负责把底层的 `Dna.Knowledge` 封装成带锁的、受限的任务容器，供上层的内置 Agent、外置 Agent（Cursor/Claude）和 CLI 使用。

**核心哲学**：Workbench 不负责替 Agent 思考（不编排），它只负责**供能与限域**（提供精准上下文，限制修改边界）。

### 边界声明

- `Dna.Workbench` **没有**大模型能力。
- `Dna.Workbench` **不做**自然语言理解、语义规划、任务拆解或多步决策。
- `Dna.Workbench` 只做确定性能力：模块候选检索、治理范围解析、上下文裁剪、模块锁、任务开始/结束与结果回写。
- 真正的理解、规划、排序和取舍属于 `Dna.Agent` 或外置 Agent。

## 2. 核心架构机制

Workbench 的实现围绕以下三大核心机制展开：

### 2.1 统一的生命周期与模块锁 (Unified Lifecycle & Locks)
**无论是正常的业务需求（Requirement）还是周期性的架构治理（Governance），在执行阶段都统一抽象为标准的 Task。它们共用完全相同的生命周期和锁机制。**
- **机制**：一个 Task 只能绑定一个目标模块。在 `StartTask` 时，系统向 `ModuleLockManager` 申请该模块的排他锁。
- **效果**：如果 Cursor 正在修改 `Payment` 模块（无论是为了开发新功能还是为了重构），Claude Code 尝试启动 `Payment` 模块的任何任务都会被直接拒绝（Fail Fast），把冲突在任务启动前拦截。
- **协作补充**：任务请求可以声明前置 task；Workbench 负责校验这些前置 task 是否已经成功完成，并把前置状态回传给上层。
- **透出补充**：`StartTask` / `EndTask` 都应返回统一响应模型，而不是把异常细节直接暴露给 API / MCP 适配层。

### 2.2 视界裁剪与上下文组装 (Horizon Filtering)
Agent 不应该看到整个工程的源码，这会导致越权修改和架构漂移。
- **机制**：`TaskContextBuilder` 根据 MCDP 协议和 `dependencies.json` 动态生成上下文。
- **效果**：Agent 只能看到目标模块的完整实现；对于其依赖的底层框架，只能看到 `identity.md` 中的 `Contract`（契约接口），物理屏蔽底层实现。对于 Governance 类型的任务，上下文可能会额外注入待处理的架构约束。
- **关系显式化**：除可见模块外，TaskContext 还应显式返回目标模块的出边、入边以及可用协作上下文，让上层清楚“当前任务为什么能看到这些模块、处在什么关系链上”。 
- **操作空间结构化**：`WorkspaceScope` 不应只是一段字符串，而应细化为 `ReadableScopes / WritableScopes / ContractOnlyScopes`，便于外置 Agent、MCP 和 UI 直接消费。

### 2.3 需求收口辅助与治理范围解析 (Resolution Support)
Workbench 提供的是确定性的收口辅助接口，而不是替 Agent 做智能规划。
- **ResolveRequirementSupport**：输入需求文本或提示，结合 TopoGraph 和模块知识，输出相关模块候选、关系与边界提示。它只做确定性匹配，不做语义推理。
- **ResolveGovernance**：输出指定范围的治理树上下文、规则与诊断结果，不替 Agent 生成治理计划。
- **统一收口**：Agent 拿到候选或上下文后，自行决定生成哪些 `TaskRequest`，然后统一进入 `StartTask` 漏斗。

## 3. 领域模型与服务契约

Workbench 对外暴露统一的 `IWorkbenchFacade`，包含五大子域：

1. **Tasks (`IWorkbenchTaskService`)**：核心任务引擎，提供统一的 `StartTask` 和 `EndTask`。
2. **Governance (`IWorkbenchGovernanceService`)**：仅负责治理范围解析和规划。
3. **Knowledge (`IKnowledgeWorkbenchService`)**：基础工作区与知识查询。
4. **Tools (`IWorkbenchToolService`)**：统一工具注册与调用。
5. **Runtime (`IWorkbenchRuntimeService`)**：运行时拓扑事件投影。

*(详细类图见 `CLASS-DIAGRAM.md`，运行流程见 `FLOW.md`)*

## 4. 分步实施计划 (Implementation Plan)

为了保证依赖单向性和代码结构的清晰，Workbench 的实现分为 5 个阶段：

### Phase 1: 领域模型与契约定义 (Domain Models)
- 定义 `TaskType` (Requirement/Governance), `TaskRequest`, `TaskContext`, `TaskResult`, `ModuleLock` 等核心实体。
- 定义 `IWorkbenchTaskService` 和 `IWorkbenchGovernanceService` 接口。
- 将新接口集成到 `IWorkbenchFacade`。

### Phase 2: 模块锁与并发控制 (Concurrency Control)
- 实现 `IModuleLockManager`（基于 `ConcurrentDictionary` 的内存锁）。
- 提供 `TryAcquireLock` 和 `ReleaseLock`，确保“单模块单任务”的绝对互斥。

### Phase 3: 视界裁剪与上下文引擎 (Context Builder)
- 实现 `ITaskContextBuilder`。
- 桥接 `Dna.Knowledge`，根据模块的 `layerScore`、`TaskType` 和依赖树，生成经过 Horizon Filtering 裁剪的 Markdown/JSON 上下文。

### Phase 4: 任务生命周期闭环 (Task Lifecycle)
- 实现 `WorkbenchTaskService`。
- 串联 `StartTask`（校验前置依赖 -> 获取锁 -> 组装上下文 -> 记录活动任务）和 `EndTask`（接收结果 -> 写入 Memory -> 释放锁 -> 产出完成回执）。
- 为上层补充活动任务列表与最近完成任务列表，支撑协作、恢复与 UI 展示。

### Phase 5: 需求收口辅助与治理解析 (Resolution Support)
- 实现 `ResolveRequirementSupport` 和 `ResolveGovernance`。
- 保持 Workbench 只负责模块候选解析、范围收口和确定性上下文供给，不承担大模型规划职责。

## 5. 约束与规范

1. **单向依赖**：Workbench 只能依赖 `Dna.Knowledge` 和 `Dna.Core`，绝对不能反向依赖 `Dna.Agent` 或 `App`。
2. **状态管理**：模块锁和活动任务状态保存在内存中（由 `App` 托管单例），但长期记忆（Memory）必须通过 `Dna.Knowledge` 持久化到 `.agentic-os/` 目录。
3. **历史代码隔离**：旧的 `src/Dna.Workbench/Agent/Pipeline/*` 代码属于废弃的实验性管线，新功能严禁在此处堆砌，必须按上述 5 大子域重新组织。