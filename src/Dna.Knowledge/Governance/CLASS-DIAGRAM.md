# Dna.Knowledge.Governance 类图

> 状态：重构后稳定类图
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Governance`

## 目标类图

```mermaid
classDiagram
    class IGovernanceEngine {
        <<interface>>
        +ValidateArchitecture() GovernanceReport
        +CheckFreshness() int
        +DetectMemoryConflicts() int
        +ArchiveStaleMemories(staleThreshold) int
        +EvolveKnowledgeAsync(nodeIdOrName, maxSuggestions) Task~KnowledgeEvolutionReport~
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
        +EvolveKnowledgeAsync(nodeIdOrName, maxSuggestions) Task~KnowledgeEvolutionReport~
        +CondenseNodeKnowledgeAsync(nodeIdOrName, maxSourceMemories) Task~KnowledgeCondenseResult~
        +CondenseAllNodesAsync(maxSourceMemories) Task~List~KnowledgeCondenseResult~~
    }

    class KnowledgeEvolutionReport {
        +DateTime GeneratedAt
        +string? FilterNodeId
        +string? FilterNodeName
        +int SessionToMemoryCount
        +int MemoryToKnowledgeCount
        +List~KnowledgeEvolutionSuggestion~ Suggestions
    }

    class KnowledgeEvolutionSuggestion {
        +string MemoryId
        +string? NodeId
        +string? NodeName
        +EvolutionKnowledgeLayer CurrentLayer
        +EvolutionKnowledgeLayer TargetLayer
        +string Reason
        +double Confidence
        +List~string~ CandidateModuleIds
        +List~string~ CandidateModuleNames
    }

    class KnowledgeCondenseResult {
        +string NodeId
        +string? NodeName
        +int SourceCount
        +int SessionSourceCount
        +int MemorySourceCount
        +int ArchivedCount
        +string? NewIdentityMemoryId
        +string? UpgradeTrailMemoryId
        +List~string~ SessionSourceMemoryIds
        +List~string~ MemorySourceMemoryIds
        +List~string~ ArchivedMemoryIds
        +string? Summary
    }

    class NodeKnowledge {
        +string? Identity
        +List~LessonSummary~ Lessons
        +List~string~ ActiveTasks
        +List~string~ Facts
        +int TotalMemoryCount
        +string? IdentityMemoryId
        +string? UpgradeTrailMemoryId
        +List~string~ MemoryIds
    }

    class ITopoGraphStore {
        <<external>>
        +ResolveNodeIdCandidates(nodeId, strict) List~string~
        +LoadNodeKnowledgeMap() Dictionary~string, NodeKnowledge~
        +UpsertNodeKnowledge(nodeId, knowledge) void
    }

    class ITopoGraphApplicationService {
        <<external>>
        +GetManagementSnapshot() TopologyManagementSnapshot
        +ValidateArchitecture() GovernanceReport
        +BuildTopology() TopologySnapshot
    }

    class MemoryStore {
        <<external>>
        +Query(filter) List~MemoryEntry~
        +RememberAsync(request) Task~MemoryEntry~
        +UpdateTags(id, tags) void
        +UpdateFreshness(id, freshness) void
    }

    IGovernanceEngine <|.. GovernanceEngine
    GovernanceEngine --> FreshnessChecker : delegates freshness
    GovernanceEngine --> MemoryMaintainer : delegates evolve / condense
    GovernanceEngine --> ITopoGraphApplicationService : validates architecture

    FreshnessChecker --> MemoryStore : decays freshness

    MemoryMaintainer --> MemoryStore : reads / writes memories
    MemoryMaintainer --> ITopoGraphStore : resolves node ids / writes node knowledge
    MemoryMaintainer --> ITopoGraphApplicationService : reads management snapshot
    MemoryMaintainer --> KnowledgeEvolutionReport : returns
    MemoryMaintainer --> KnowledgeCondenseResult : returns
    KnowledgeEvolutionReport *-- KnowledgeEvolutionSuggestion : contains
    ITopoGraphStore --> NodeKnowledge : persists
```

## 说明

- `evolve` 是治理建议接口，只分析升级机会，不直接落库。
- `condense` 是治理执行接口，负责真正生成 identity、upgrade trail，并归档已沉淀记忆。
- `Governance` 不拥有拓扑定义，只消费 `ITopoGraphApplicationService` 与 `ITopoGraphStore`。
- `NodeKnowledge` 已稳定持久化 `IdentityMemoryId` 与 `UpgradeTrailMemoryId`，用于 `.agentic-os/knowledge/modules/<uid>/identity.md` 回写。
