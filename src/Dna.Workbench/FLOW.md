# Dna.Workbench 运行流程图 (Runtime Flow)

> 状态：Active
> 最后更新：2026-04-04
> 说明：描述 Agent 与 Workbench 交互的标准任务闭环。无论是需求开发还是周期治理，在执行阶段都走完全相同的 Start -> Lock -> End 流程。

## 1. 统一任务闭环 (Unified Task Lifecycle)

无论是 `Requirement` 还是 `Governance`，区别仅在于前期的“候选收口 / Agent 规划”阶段，一旦进入执行阶段，全部收口为标准的 `TaskRequest`。

```mermaid
sequenceDiagram
    autonumber
    actor Agent as Built-in/External Agent
    participant Facade as IWorkbenchFacade
    participant GovSvc as IWorkbenchGovernanceService
    participant TaskSvc as IWorkbenchTaskService
    participant LockMgr as IModuleLockManager
    participant CtxBldr as ITaskContextBuilder
    participant Knowledge as Dna.Knowledge

    %% 阶段 1：候选收口 + Agent 规划 (二选一)
    rect rgb(240, 248, 255)
        Note over Agent, Knowledge: Phase 1: 候选收口 (Workbench) + 规划 (Agent) - 需求或治理二选一
        alt 是业务需求 (Requirement)
            Agent->>Facade: Tasks.ResolveRequirementSupport("实现支付功能")
            Facade->>TaskSvc: ResolveRequirementSupport
            TaskSvc->>Knowledge: 查询 TopoGraph 与模块知识
            TaskSvc-->>Agent: 返回 TaskCandidate[] 与边界提示
        else 是周期治理 (Governance)
            Agent->>Facade: Governance.ResolveGovernance("Program/Business")
            Facade->>GovSvc: ResolveGovernance
            GovSvc->>Knowledge: 提取模块树与待处理 Memory
            GovSvc-->>Agent: 返回 GovernanceContext
        end
    end

    Note over Agent: Agent 根据返回的候选或上下文<br/>自行决定要创建哪些单模块 TaskRequest<br/>(Type = Requirement 或 Governance)<br/>Workbench 不参与任务链规划

    %% 阶段 2：任务启动与锁定 (统一流程)
    rect rgb(255, 240, 245)
        Note over Agent, Knowledge: Phase 2: 任务启动与锁定 (Task Start & Lock) - 统一流程
        Agent->>Facade: Tasks.StartTask(TaskRequest)
        Facade->>TaskSvc: StartTask
        TaskSvc->>TaskSvc: 校验前置 task 是否已成功完成
        alt 前置依赖未满足
            TaskSvc-->>Agent: 返回 prerequisite not satisfied
        else 模块空闲
            TaskSvc->>LockMgr: TryAcquireLock(TaskRequest.ModuleId)
            alt 模块已被占用
                LockMgr-->>TaskSvc: false
                TaskSvc-->>Agent: 抛出 ModuleLockedException (拒绝执行)
            else 获取锁成功
            LockMgr-->>TaskSvc: true
            TaskSvc->>CtxBldr: BuildContext(TaskRequest)
            CtxBldr->>Knowledge: 获取该模块 DNA、上游 Contract、相关 Memory
            CtxBldr-->>TaskSvc: 组装受限的 TaskContext (Horizon Filtered + Relations)
            TaskSvc-->>Agent: 返回 TaskStartResponse
            end
        end
    end

    %% 阶段 3：受控执行
    Note over Agent: Agent 在 TaskContext 划定的<br/>唯一模块和封闭空间内执行代码修改/重构

    %% 阶段 4：任务结束与知识回写 (统一流程)
    rect rgb(240, 255, 240)
        Note over Agent, Knowledge: Phase 4: 任务结束与回写 (Task End & Writeback) - 统一流程
        Agent->>Facade: Tasks.EndTask(TaskResult)
        Facade->>TaskSvc: EndTask
        TaskSvc->>Knowledge: 沉淀 Decisions, Lessons 到 Memory
        TaskSvc->>LockMgr: ReleaseLock(TaskResult.ModuleId)
        TaskSvc-->>Agent: 返回 TaskEndResponse
    end
```

## 核心机制说明

1. **统一的执行漏斗**：无论 Agent 想要做什么（开发新功能、修 Bug、重构、清理依赖），最终都必须构造一个 `TaskRequest` 并调用 `StartTask`。
2. **Fail Fast 机制**：如果多个 Agent（或同一个 Agent 的并发线程）尝试 `StartTask` 同一个模块，`LockMgr` 会直接拒绝后续请求，强制 Agent 重新编排或等待。
3. **视界裁剪 (Horizon Filtering)**：`ITaskContextBuilder` 是关键，它不会把整个工作区的代码给 Agent，而是只给目标模块的完整实现，以及依赖模块的 `Contract`（契约）。如果是 `Governance` 类型的任务，Context 中可能会额外强调架构约束。
4. **闭环强制性**：Agent 必须调用 `EndTask` 才能释放锁，并且必须在 `TaskResult` 中提交决策和教训，强制完成知识沉淀。
5. **可协作性**：Workbench 维护活动任务与最近完成任务摘要，供上层 Agent Runtime、外置 Agent 和 UI 做恢复、依赖校验与状态展示。