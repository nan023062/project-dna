# Dna.Knowledge.Governance 类图

> 状态：重构后类图基线
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Governance`

本文档只描述治理模块当前稳定的责任分层。

## 目标类图

```mermaid
classDiagram
    class IGovernanceEngine {
        <<interface>>
        +ValidateArchitecture() GovernanceReport
        +CheckFreshness() int
        +DetectMemoryConflicts() int
        +ArchiveStaleMemories(staleThreshold) int
        +CondenseNodeKnowledgeAsync(nodeIdOrName, maxSourceMemories) Task~KnowledgeCondenseResult~
        +CondenseAllNodesAsync(maxSourceMemories) Task~List~KnowledgeCondenseResult~~
    }

    class GovernanceEngine {
        -ITopoGraphApplicationService topology
        -FreshnessChecker freshnessChecker
        -MemoryMaintainer memoryMaintainer
    }

    class FreshnessChecker {
        -MemoryStore store
        +CheckAll() int
    }

    class MemoryMaintainer {
        -MemoryStore memoryStore
        -ITopoGraphStore topoGraphStore
        -ITopoGraphApplicationService topology
        +DetectConflicts() int
        +ArchiveStaleMemories(staleThreshold) int
        +CondenseNodeKnowledgeAsync(nodeIdOrName, maxSourceMemories) Task~KnowledgeCondenseResult~
        +CondenseAllNodesAsync(maxSourceMemories) Task~List~KnowledgeCondenseResult~~
    }

    class GovernanceTargetNode {
        <<internal record>>
        +string Id
        +string Name
        +NodeType Type
        +string? Discipline
        +string? Summary
        +string? Contract
    }

    class TopologyManagementSnapshot {
        +List~TopologyModuleDefinition~ Modules
    }

    class TopologyModuleDefinition {
        +string Id
        +string Name
        +string Discipline
        +int Layer
        +bool IsCrossWorkModule
        +string? Summary
        +string? Boundary
    }

    class KnowledgeCondenseResult {
        +string NodeId
        +string? NodeName
        +int SourceCount
        +int ArchivedCount
        +string? NewIdentityMemoryId
        +string? Summary
    }

    class MemoryStore {
        <<external>>
        +DecayStaleMemories() int
        +Query(filter) List~MemoryEntry~
        +UpdateTags(id, tags) void
        +UpdateFreshness(id, freshness) void
        +RememberAsync(request) Task~MemoryEntry~
    }

    class ITopoGraphStore {
        <<external>>
        +ResolveNodeIdCandidates(nodeId, strict) List~string~
        +LoadNodeKnowledgeMap() Dictionary~string, NodeKnowledge~
        +UpsertNodeKnowledge(nodeId, knowledge) void
        +GetComputedManifest() ComputedManifest
    }

    class ITopoGraphApplicationService {
        <<external>>
        +GetManagementSnapshot() TopologyManagementSnapshot
        +ValidateArchitecture() GovernanceReport
        +BuildTopology() TopologySnapshot
    }

    IGovernanceEngine <|.. GovernanceEngine

    GovernanceEngine --> FreshnessChecker : delegates freshness
    GovernanceEngine --> MemoryMaintainer : delegates maintenance
    GovernanceEngine --> ITopoGraphApplicationService : validates architecture

    FreshnessChecker --> MemoryStore : decays freshness

    MemoryMaintainer --> MemoryStore : reads/writes memories
    MemoryMaintainer --> ITopoGraphStore : resolves node ids / writes node knowledge
    MemoryMaintainer --> ITopoGraphApplicationService : reads management snapshot
    MemoryMaintainer --> GovernanceTargetNode : projects target node
    ITopoGraphApplicationService --> TopologyManagementSnapshot : returns
    TopologyManagementSnapshot --> TopologyModuleDefinition : contains
```

## 类图说明

- `GovernanceEngine` 只做门面和编排。
- `FreshnessChecker` 只负责鲜活度衰减。
- `MemoryMaintainer` 负责冲突检测、归档和知识压缩，但不拥有拓扑定义。
- `ITopoGraphStore` 已收敛为轻量运行时仓库，只承担候选节点解析、计算依赖和节点知识写入。
- `ITopoGraphApplicationService` 是治理读取管理模型与执行架构校验的唯一拓扑入口。
- `GovernanceTargetNode` 是治理内部投影视图，来自 `TopologyManagementSnapshot.Modules`，不是新的注册模型。

## 约束

1. `Governance` 不重新引入图谱定义、模块注册或兼容运行时。
2. `MemoryMaintainer` 必须通过 `GetManagementSnapshot()` 获取治理目标，而不是自行读取定义文件。
3. `ValidateArchitecture()` 只能走 `ITopoGraphApplicationService`。
4. 模块知识沉淀必须回写 `ITopoGraphStore.UpsertNodeKnowledge(...)`。
