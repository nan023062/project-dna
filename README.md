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

Project DNA now uses a **server + client split**:
- Server stores structure, memory, and relationships as a queryable knowledge graph.
- Client hosts MCP and decision/execution entry points, and proxies requests to the server.

```
AI Agents / IDEs
        ↓ MCP
Project DNA Client (MCP + agent entry)
        ↓ HTTP API
Project DNA Server (knowledge graph + memory)
        ↓
Context-aware modifications
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

The server is a pure knowledge service — it does **not** access project source code.  
MCP now lives in the client app, which forwards requests to server APIs.

```
┌──────────────────────────────────────────────────┐
│                DNA Server                          │
│                                                    │
│  HTTP API    CLI          Dashboard               │
│  /api/*      dna cli      browser                 │
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

### 2026-03 Sync Notes

- DB-first storage is now the default and only path: graph and memory are stored in SQLite.
- The graph no longer relies on `architecture.json` or `modules.json`.
- Memory no longer writes `memory/entries/*.json`; all entries are persisted in DB.
- Knowledge condensation is available: short-term memories are distilled into long-term `NodeKnowledge`.
- Scheduled full condensation can be configured via API and Dashboard.

## Quick Start

### 1. Build

```bash
cd src
dotnet build
```

### 2. Run

```bash
# 1) Start knowledge server
cd /path/to/knowledge-store && dna --db       # current dir as store
dna --db /path/to/knowledge-store             # specify store path

# 2) Start client (MCP + agent entry)
Client --server http://localhost:5051
Client --stdio --server http://localhost:5051    # stdio mode for IDE
```

`--db` is required. The server stores all data (SQLite) in this directory.
Current MVP uses fixed ports and single-instance runtime: `Server=5051`, `Client=5052`.

### 3. Connect from Cursor / Codex

`.cursor/mcp.json` or `.codex/mcp.json`:

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

Use the Client MCP endpoint for IDE connections.  
Recommended topology: `Server(5051)` + `Client(5052)`.

### 4. Dashboard

Open `http://localhost:5051` in your browser:
- Topology visualization (read-only)
- Memory CRUD (create, search, edit, delete)
- LLM configuration and AI chat

Open `http://localhost:5052` for the headless client workbench (optional web mode).

### 4.1 Desktop Client (Integrated)

Desktop window mode is now integrated into `src/Client` (single project + single process):

```bash
dotnet run --no-launch-profile --project src/Client
```

Current desktop capabilities:
- Select target project at startup (project root must contain `.project.dna`)
- Read `projectName` and `serverBaseUrl` from `.project.dna` to build workspace config
- Start/stop embedded local `Client` host (`5052`) in the same process
- Overview / Topology / Memory / Tooling+MCP tabs in window
- MCP endpoint copy and one-click Cursor/Codex workflow installation

`.project.dna` example:

```json
{
  "projectName": "agentic-os",
  "serverBaseUrl": "http://127.0.0.1:5051"
}
```

Headless mode is still available for automation and IDE MCP transport:

```bash
dotnet run --no-launch-profile --project src/Client -- web --server http://127.0.0.1:5051
```

### 5. CLI

```bash
dna cli status                          # Server status
dna cli validate                        # Architecture health check
dna cli search combat                   # Search modules
dna cli recall "what constraints apply" # Semantic memory search
dna cli stats                           # Knowledge base statistics
```

### 6. Client Execution Pipeline (Architect -> Developer)

Client now provides a configurable execution pipeline with default order "retrospect first, then develop":

- Read config: `GET /api/client/pipeline/config`
- Update config: `PUT /api/client/pipeline/config`
- Run pipeline: `POST /api/client/pipeline/run`
- Latest run: `GET /api/client/pipeline/runs/latest`

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
| `condense_module_knowledge` | Condense one module's knowledge into `NodeKnowledge` |
| `condense_all_module_knowledge` | Run full condensation for all modules |
| `get_execution_pipeline_config` | Get client execution pipeline config |
| `update_execution_pipeline_config` | Update client execution pipeline config |
| `run_execution_pipeline` | Run architect->developer pipeline |
| `get_latest_pipeline_run` | Get latest pipeline run result |

Client also exposes `GET /api/client/mcp/tools` to return the full MCP tool catalog (including parameter descriptions) for UI and automation.

## 2026-03 Client/Server Split Notes

- `Server` is now a standalone knowledge service (`REST API + Dashboard + SQLite`).
- `Client` is now a standalone MCP gateway and execution entry.
- Why split: multi-end knowledge writes need online conflict-safe coordination; Git/P4 are great for source control, but not ideal as the runtime knowledge write arbiter.
- Transport, auth, and real-time delivery decision: [docs/architecture/project-dna-transport-auth-decision.md](docs/architecture/project-dna-transport-auth-decision.md)

## 2026-04 Connection & Tooling Convergence Notes

- Login flow is removed from current MVP path; access is controlled by server IP allowlist with per-IP role assignment.
- Client no longer performs automatic LAN server scanning; users connect by manually entering server IP/URL.
- Client only displays current permission profile and cannot modify roles.
- IDE tooling install flow is upgraded: choose target project folder via system picker, then install Cursor/Codex workflow files into that folder.

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
