# Project DNA

**The cognitive engine for your project.**

Mem0 remembers conversations. GitNexus reads code syntax. CLAUDE.md stores personal notes.
**Project DNA understands your entire project** — structure, knowledge, dependencies, constraints, and cross-team collaboration — so AI agents can modify files with full context.

[中文文档](README.zh-CN.md)

---

## The Problem

AI agents are getting better at writing code. But they still lack **project-level cognition**:

- They don't know which module they're working in, or what constraints apply
- They can't see cross-file dependencies at an architectural level
- They don't remember past lessons or team conventions
- They have no concept of cross-team collaboration (code + art + design)

**Result**: Agents make changes that compile but violate project architecture, break implicit contracts, or ignore hard-won lessons.

## The Solution

Project DNA extracts the **structure, knowledge, and relationships** from your project's file system into a queryable knowledge graph. Agents read the DNA before touching any file.

```
File System (thousands of files)
        ↓ extract
Project DNA (dozens of knowledge nodes)
        ↓ serve via MCP
AI Agent (precise, context-aware file modifications)
```

## Key Concepts

### One Node Type, All Organizational Forms

Every entity — module, team, department, workspace, cross-functional task force — is the same `KnowledgeNode` with a `NodeType`:

| NodeType | Software Analogy | Organization Analogy |
|----------|-----------------|---------------------|
| `Root` | System | Studio / Project |
| `Department` | Package | Department / Division |
| `Module` | Class / Service | Team / Specialist |
| `CrossWork` | Composite | Task Force / Working Group |

### Three Orthogonal Relationships

| Relationship | What it models | Rule |
|-------------|---------------|------|
| **Containment** (tree) | Organizational hierarchy | Each node has one parent |
| **Dependency** (DAG) | "I consume your output" | No cycles; freely crosses org boundaries |
| **Collaboration** (CrossWork) | "We deliver together" | Multi-party, not a substitute for dependency |

### Every Node Carries Knowledge

Each node has a materialized `Knowledge` view — identity, constraints, lessons learned, active tasks. One MCP call returns the full context. No need for multiple queries.

### Cycles = Merge Signal

Circular dependencies are not violations — they're **restructuring suggestions**. If A depends on B and B depends on A, they should be one node or a working group.

## Architecture

Three independent engines:

```
┌─────────────────────────────────────────┐
│          Project DNA Server (MCP)       │
│                                         │
│  GraphEngine    MemoryEngine  Governance│
│  (nodes, edges, (remember,    (cycle    │
│   topology,      recall,      detection,│
│   context)       query)       freshness)│
│                                         │
│            Storage Layer                │
│     SQLite + JSON + Manifests           │
│         {project}/.dna/                 │
└─────────────────────────────────────────┘
```

- **GraphEngine** — Node/edge CRUD, topology, execution plans, module context
- **MemoryEngine** — Knowledge storage & retrieval (vector + FTS + tag + coordinate search)
- **GovernanceEngine** — Architecture advisor (not rule police): cycle detection, key node warnings, freshness checks

## Quick Start

### 1. Build

```bash
cd src
dotnet build
```

### 2. Run

```bash
# HTTP mode (team sharing via dashboard)
dotnet run --project Server -- --project /path/to/your/project

# stdio mode (single user, launched by IDE)
dotnet run --project Server -- --stdio --project /path/to/your/project
```

### 3. Connect from Cursor

Create `.cursor/mcp.json` in your project root:

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5051/mcp"
    }
  }
}
```

### 4. Verify

In Cursor Agent, say:

```
Show me the project knowledge graph status
```

If the agent calls `get_project_identity` and returns project info, you're connected.

## MCP Tools

### Graph Tools

| Tool | Description |
|------|-------------|
| `get_project_identity` | Verify project binding (mandatory first call) |
| `get_topology` | Full project structure overview |
| `begin_task` | Get module context before modifying files |
| `find_modules` | Search nodes by keyword |
| `get_execution_plan` | Topological sort for multi-module changes |
| `register_module` | Add a knowledge node |
| `register_crosswork` | Declare cross-team collaboration |
| `validate_architecture` | Architecture health check |

### Memory Tools

| Tool | Description |
|------|-------------|
| `remember` | Store knowledge (assigned to a node) |
| `recall` | Semantic search across knowledge base |
| `batch_remember` | Bulk knowledge ingestion |
| `query_memories` | Structured query with filters |

## Ecosystem (Planned)

Project DNA is designed as an extensible platform:

| Extension Type | What community contributes |
|---------------|---------------------------|
| **Templates** | Pre-built DNA for project types (Unity, React, Spring Boot...) |
| **Scanners** | Auto-detect project structure from file system |
| **Governance Rules** | Architecture best practices per tech stack |
| **Knowledge Extractors** | Import from Confluence, Jira, Swagger... |

## Design Philosophy

> **Simplicity is the ultimate sophistication.**
>
> One node type. Three relationships. No layers to declare.
> Dependencies flow freely — cycles are merge signals, not violations.
> All organizational forms — modules, teams, departments, task forces —
> are the same pattern at different scales.

Read the full design document: [docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)

## Comparison

| Capability | Mem0 | Zep | GitNexus | Pensieve | **DNA** |
|-----------|------|-----|----------|----------|---------|
| Project structure modeling | - | - | Code-level | - | **Org-level** |
| Multi-discipline (code+art+design) | - | - | - | - | **Yes** |
| Knowledge storage & retrieval | Yes | Yes | - | Basic | **4-channel** |
| Architecture governance | - | - | - | - | **Yes** |
| Team collaboration | Org memory | Yes | - | - | **CrossWork** |
| Multi-role perspectives | - | - | - | - | **Interpreters** |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on creating templates, scanners, and governance rules.

## License

[MIT](LICENSE)
