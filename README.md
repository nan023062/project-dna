# Project DNA

**The knowledge engine for AI agents.**

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

Project DNA is a **knowledge server** that stores structure, knowledge, and relationships as a queryable graph. Agents query the server before touching any file.

```
Clients (IDE / CLI / Dashboard)
        ↓ MCP / HTTP API
Project DNA Server (knowledge graph + memory)
        ↓
AI Agent makes context-aware modifications
```

## Key Concepts

### One Node Type, All Organizational Forms

Every entity — module, team, department, cross-functional task force — is the same `KnowledgeNode` with a `NodeType`:

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

Each node has a materialized `Knowledge` view — identity, constraints, lessons learned, active tasks. One MCP call returns the full context.

### Cycles = Merge Signal

Circular dependencies are not violations — they're **restructuring suggestions**. If A depends on B and B depends on A, they should be one node or a working group.

## Architecture

The server is a pure knowledge service — it does **not** access project source code. Clients write knowledge via MCP or HTTP API.

```
┌──────────────────────────────────────────────────┐
│                DNA Server                          │
│                                                    │
│  HTTP API    MCP Server    CLI        Dashboard   │
│  /api/*      /mcp          dna cli    browser     │
│                                                    │
│  GraphEngine    MemoryEngine    GovernanceEngine   │
│                                                    │
│  Storage: SQLite (single source of truth)          │
│  Auth: JWT + roles (admin/editor/viewer)           │
└──────────────────────────────────────────────────┘
```

- **GraphEngine** — Node CRUD, topology, dependency ordering, module context
- **MemoryEngine** — Knowledge storage & retrieval (vector + FTS + tag + coordinate search)
- **GovernanceEngine** — Architecture advisor: cycle detection, key node warnings, freshness checks

## Quick Start

### 1. Build

```bash
cd src
dotnet build
```

### 2. Run

```bash
cd /path/to/knowledge-store && dna --db       # current dir as store
dna --db /path/to/knowledge-store             # specify store path
dna --db /path/to/store --port 5052           # custom port
dna --stdio --db /path/to/store               # stdio mode for IDE
```

`--db` is required. The server stores all data (SQLite) in this directory.

### 3. Connect from Cursor / Codex

`.cursor/mcp.json` or `.codex/mcp.json`:

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5051/mcp"
    }
  }
}
```

The server prints its LAN IP on startup — use that for remote connections.

### 4. Dashboard

Open `http://localhost:5051` in your browser:
- Topology visualization (read-only)
- Memory CRUD (create, search, edit, delete)
- LLM configuration and AI chat

### 5. CLI

```bash
dna cli status                          # Server status
dna cli validate                        # Architecture health check
dna cli search combat                   # Search modules
dna cli recall "what constraints apply" # Semantic memory search
dna cli stats                           # Knowledge base statistics
```

## MCP Tools

### Graph Tools

| Tool | Description |
|------|-------------|
| `get_topology` | Full knowledge graph overview |
| `get_context` | Get module context (constraints, dependencies, CrossWork, lessons) |
| `search_modules` | Search nodes by keyword |
| `get_dependency_order` | Topological sort for multi-module changes |
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
| `get_memory` | Get full memory entry by ID |
| `get_memory_stats` | Knowledge base statistics |
| `verify_memory` | Confirm a memory is still valid |
| `update_memory` | Update existing memory |
| `delete_memory` | Delete a memory |

## Ecosystem (Planned)

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

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## License

[Apache 2.0](LICENSE)
