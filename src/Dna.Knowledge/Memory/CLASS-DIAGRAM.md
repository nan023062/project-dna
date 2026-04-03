# Dna.Knowledge.Memory 类图

> 状态：目标重构类图
> 最后更新：2026-04-03
> 适用范围：`src/Dna.Knowledge/Memory`

## 目标类图

```mermaid
classDiagram
    class IMemoryEngine {
        <<interface>>
        +RememberAsync(request) Task~MemoryEntry~
        +UpdateMemoryAsync(memoryId, request) Task~MemoryEntry~
        +DeleteMemory(id) bool
        +VerifyMemory(memoryId) void
        +RecallAsync(query) Task~RecallResult~
        +QueryMemories(filter) List~MemoryEntry~
        +GetMemoryById(id) MemoryEntry
        +GetMemoryStats() MemoryStats
        +DecayStaleMemories() int
    }

    class MemoryEngine {
        -MemoryWriter writer
        -MemoryReader reader
        -MemoryRecallEngine recallEngine
    }

    class MemoryWriter {
        -MemoryStore store
        -EmbeddingService embeddingService
        -VectorIndex vectorIndex
    }

    class MemoryReader {
        -MemoryStore store
    }

    class MemoryRecallEngine {
        -MemoryStore store
        -EmbeddingService embeddingService
        -VectorIndex vectorIndex
    }

    class MemoryStore {
        +Initialize(storePath)
        +Insert(entry) MemoryEntry
        +Update(entry) MemoryEntry
        +Delete(id) bool
        +GetById(id) MemoryEntry
        +Query(filter) List~MemoryEntry~
        +FullTextSearch(query, limit) List~(Id, Rank)~
    }

    class SessionFileStore {
        +SaveSession(agenticOsPath, file) void
        +LoadSessions(agenticOsPath) List~SessionFile~
    }

    class SessionFile {
        +string Id
        +string Type
        +string Source
        +string? NodeId
        +List~string~ Tags
        +string Category
        +DateTime CreatedAt
        +string Body
    }

    class MemoryEntry {
        +string Id
        +MemoryType Type
        +MemorySource Source
        +string Content
        +string? Summary
        +double Importance
        +string? NodeId
        +List~string~ Tags
        +MemoryStage Stage
        +FreshnessStatus Freshness
    }

    class MemoryStage {
        <<enumeration>>
        ShortTerm
        LongTerm
    }

    IMemoryEngine <|.. MemoryEngine

    MemoryEngine --> MemoryWriter : delegates write
    MemoryEngine --> MemoryReader : delegates read
    MemoryEngine --> MemoryRecallEngine : delegates recall

    MemoryWriter --> MemoryStore : persists
    MemoryReader --> MemoryStore : queries
    MemoryRecallEngine --> MemoryStore : recalls

    MemoryStore --> SessionFileStore : rebuilds / syncs short-term state
    SessionFileStore --> SessionFile : persists .agentic-os/session/tasks|context
    MemoryStore --> MemoryEntry : stores
    MemoryEntry --> MemoryStage : staged by
```

## 说明

- `ShortTerm` 记忆会同步到 `.agentic-os/session/tasks|context`。
- `LongTerm` 记忆会同步到 `.agentic-os/memory/*`。
- `MemoryStore` 启动时会同时从 `memory` 和 `session` 两套文件协议重建统一内存视图。
- `Memory` 不承担拓扑定义，也不承担模块知识最终落库；这些职责继续归 `TopoGraph` / `Governance`。
