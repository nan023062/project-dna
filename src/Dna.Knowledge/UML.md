# Dna.Knowledge UML 评审图

> 状态：重构后评审基线
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge`

本文档只保留知识域的包级与关系级视图。各子模块的细节类图以下沉文档为准。

## 1. 包图

```mermaid
flowchart TD
    Host["宿主层<br/>App / CLI / MCP"]

    subgraph Knowledge["Dna.Knowledge Department"]
        Root["Dna.Knowledge<br/>父级组织与装配入口"]

        subgraph GovernancePkg["Governance"]
            Governance["GovernanceEngine"]
        end

        subgraph MemoryPkg["Memory"]
            Memory["MemoryEngine"]
            MemoryStore["MemoryStore"]
        end

        subgraph TopoPkg["TopoGraph"]
            TopoApp["TopoGraphApplicationService"]
            TopoFacade["ITopoGraphFacade"]
            TopoStore["ITopoGraphStore"]
        end

        subgraph WorkspacePkg["Workspace"]
            Workspace["WorkspaceEngine"]
            WorkspaceMeta[".agentic.meta"]
        end

        Files[".agentic-os/memory<br/>.agentic-os/knowledge/modules"]
    end

    Host --> Root
    Root --> Governance
    Root --> Memory
    Root --> TopoApp
    Root --> Workspace

    Governance --> Memory
    Governance --> TopoApp
    Memory --> MemoryStore
    TopoApp --> TopoFacade
    TopoApp --> TopoStore
    TopoApp --> Workspace
    MemoryStore --> Files
    TopoFacade --> Files
    Workspace --> WorkspaceMeta
```

### 包图解读

- `Dna.Knowledge` 只负责组织与装配。
- `Governance -> Memory -> TopoGraph -> Workspace` 仍然是固定单向依赖链。
- `TopoGraphApplicationService` 负责对宿主层暴露统一拓扑能力。
- `ITopoGraphFacade + FileProtocol` 负责定义与快照构建，`ITopoGraphStore` 只保留轻量运行时仓库职责。
- `.agentic-os/memory` 与 `.agentic-os/knowledge/modules` 是新的文件落点。
- `.agentic.meta` 只属于物理目录元数据，不属于知识图谱定义。

## 2. Technical 语义图

```mermaid
classDiagram
    class Technical {
        <<concept>>
        +Id
        +Name
        +Summary
        +Boundary
        +PublicApi
        +Constraints
        +MainPath
        +ManagedPaths
    }

    class TopologyModuleDefinition {
        +string Id
        +string Name
        +string Path
        +int Layer
        +List~string~ ManagedPaths
        +List~string~ Dependencies
        +string? Summary
        +string? Boundary
        +List~string~ PublicApi
        +List~string~ Constraints
    }

    class KnowledgeNode {
        +string Id
        +string Name
        +NodeType Type
        +List~string~ Dependencies
        +List~string~ ComputedDependencies
        +string RelativePath
        +List~string~ ManagedPathScopes
        +NodeKnowledge Knowledge
    }

    class NodeKnowledge {
        +string? Identity
        +List~LessonSummary~ Lessons
        +List~string~ ActiveTasks
        +List~string~ Facts
        +List~string~ MemoryIds
    }

    class WorkspaceDirectoryMetadataDocument {
        +string Schema
        +string StableGuid
        +string Summary
        +DateTime UpdatedAtUtc
    }

    Technical <.. TopologyModuleDefinition : declared by
    Technical <.. KnowledgeNode : materialized as
    KnowledgeNode *-- NodeKnowledge : owns
    KnowledgeNode ..> WorkspaceDirectoryMetadataDocument : maps to physical directory
```

### Technical 语义解读

- `TopologyModuleDefinition` 是管理层定义，不再使用旧注册模型命名。
- `KnowledgeNode` 是运行时拓扑视图。
- `NodeKnowledge` 是治理沉淀结果，来源于记忆压缩，不是定义文件本身。

## 3. TopoGraph 关系图

```mermaid
flowchart TD
    Project["Project"]
    Department["Department"]
    TechnicalA["Technical"]
    TechnicalB["Technical"]
    TeamA["Team"]
    TeamB["Team"]

    Project -->|containment| Department
    Department -->|containment| TechnicalA
    Department -->|containment| TechnicalB
    Department -->|containment| TeamA
    Department -->|containment| TeamB

    TechnicalB -.->|dependency| TechnicalA
    TeamA -.->|dependency| TechnicalA
    TeamB ==>|collaboration| TeamA
```

### 关系约束

- `containment` 表达主树归属关系。
- `dependency` 表达技术依赖，允许 `Technical -> Technical` 和 `Team -> Technical`。
- `collaboration` 表达跨工作协作，不用依赖边伪装。

## 4. 当前结论

- 根级 `Dna.Knowledge` 不再维护旧运行时入口。
- 拓扑定义、模块注册和知识落位已经统一到 `TopoGraphApplicationService + ITopoGraphFacade + FileProtocol`。
- 记忆与知识的最终文件位置已经与模块职责对齐。
