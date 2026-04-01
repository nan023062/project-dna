# Project DNA

**The knowledge engine for AI agents.**

Project DNA gives agents project-level cognition: structure, dependencies, constraints, decisions, and lessons learned.

[中文文档](README.zh-CN.md)

## What It Is

Project DNA currently uses a **Server + Client desktop host** split:

- `Server` is the shared knowledge service. It stores graph, memory, governance data, and serves the management dashboard.
- `Client` is a **single-process, single-window desktop host**. It loads one project, previews topology and knowledge, and exposes the local MCP endpoint for IDE agents.
- The local `5052` surface is **embedded inside the desktop Client process**. It is not a second product, not a second host, and not a separate browser workbench.

```text
Cursor / Codex / Other IDE Agents
                |
                | MCP
                v
Project DNA Client Desktop (single window)
                |
                | local embedded API / MCP on :5052
                |
                +------> Desktop UX
                |
                +------> REST to Server
                           |
                           v
Project DNA Server (:5051)
  - Knowledge graph
  - Memory store
  - Governance engine
  - Server dashboard (wwwroot)
```

## Current MVP Focus

The current MVP is intentionally narrowed to the **single-user local admin loop**:

- Server dashboard is the main management entry.
- Client desktop is the main local workspace host.
- Access control currently centers on **server allowlist + role display**.
- Client defaults to **formal knowledge preview + local MCP access**.
- Direct formal knowledge writes are currently reserved for `admin`.
- Review flow and team-scale auth remain in the repo, but are not the main runtime path of this MVP.

## Quick Start

### 1. Build

```bash
cd src
dotnet build
```

### 2. Start the Server

`--db` is required. The server stores SQLite data in that directory.

```bash
# use current directory as the knowledge store
cd /path/to/knowledge-store
dna --db

# or specify a store path explicitly
dna --db /path/to/knowledge-store
```

Default server port: `5051`

### 3. Start the Client Desktop Host

```bash
dotnet run --no-launch-profile --project src/Client
```

Client behavior:

- Opens one desktop window
- Lets you choose a project folder that contains `.project.dna/project.json`
- Starts the embedded local Client surface on `http://127.0.0.1:5052` **after the project is loaded**
- Keeps the window lifecycle and the local MCP/API lifecycle in the same process

`.project.dna/project.json` example:

```json
{
  "projectName": "agentic-os",
  "serverBaseUrl": "http://127.0.0.1:5051"
}
```

Client-side LLM runtime reservation is stored separately in `.project.dna/llm.json`.
Client-side project logs are written to `.project.dna/logs/`.
Client-side workspace state is stored in `.project.dna/client-workspaces.json`.
Client-side local agent shell state is stored in `.project.dna/agent-shell/agent-shell-state.json`.

### 4. Connect from Cursor / Codex

First:

1. Start `Server`
2. Start the desktop `Client`
3. Load a project in the desktop window

Then point your IDE MCP config to:

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

Notes:

- `5052` is only available after the desktop Client has loaded a project.
- IDEs connect to the **desktop Client**, not directly to the Server.

## Runtime Entrances

### Server Dashboard

Open `http://localhost:5051` in your browser.

Current server-side UI focus:

- Service overview
- Connection / allowlist management
- Review queue foundation
- Topology and memory management

Server-side runtime LLM reservation is stored in `<knowledge-store>/llm/server-llm.json`.

### Client Desktop

The desktop host is the only user-facing Client runtime.

Current desktop capabilities:

- Project picker and recent projects
- Service / connection status overview
- Topology preview
- Formal knowledge preview
- Local MCP tooling entry
- One-click Cursor / Codex integration helpers

There is **no separate Client browser workbench** in the current implementation.

## CLI

Server CLI remains available for inspection and maintenance:

```bash
dna cli status
dna cli validate
dna cli search combat
dna cli recall "what constraints apply"
dna cli stats
```

## Client Runtime Surface

The embedded local Client surface currently exposes:

- MCP endpoint: `/mcp`
- Local desktop-support APIs: `/api/client/status`, `/api/client/workspaces/*`, `/api/client/tooling/*`
- Upstream proxy / query surface: `/api/status`, `/api/topology`, `/api/connection/access`, `/api/memory/*`
- Local lightweight agent shell: `/agent/*`

This surface exists to support the desktop host and IDE integrations. It is not a separate browser product.

## MCP Tools

### Graph Tools

| Tool | Description |
|------|-------------|
| `get_topology` | Full knowledge graph overview |
| `get_context` | Get module context with constraints, dependencies, and lessons |
| `search_modules` | Search nodes by keyword |
| `get_dependency_order` | Topological sort for multi-module changes |
| `register_module` | Add a knowledge node |
| `register_crosswork` | Declare cross-team collaboration |
| `validate_architecture` | Architecture health check |

### Memory Tools

| Tool | Description |
|------|-------------|
| `remember` | Store knowledge |
| `recall` | Semantic search across the knowledge base |
| `batch_remember` | Bulk knowledge ingestion |
| `query_memories` | Structured memory query |
| `get_memory` | Get a memory entry by ID |
| `get_memory_stats` | Knowledge base statistics |
| `verify_memory` | Confirm a memory is still valid |
| `update_memory` | Update a memory |
| `delete_memory` | Delete a memory |
| `condense_module_knowledge` | Condense one module into `NodeKnowledge` |
| `condense_all_module_knowledge` | Run full condensation for all modules |

The Client also exposes `GET /api/client/mcp/tools` for UI and automation usage.

## Architecture Notes

- `Server` is the source of truth for graph and memory storage.
- `Server` does not read project source files directly.
- `Client` is the local desktop host and MCP gateway.
- Team-scale auth and review architecture are still being converged, but the current MVP runtime path is local-admin first.

See:

- [docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)
- [docs/architecture/project-dna-transport-auth-decision.md](docs/architecture/project-dna-transport-auth-decision.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[Apache 2.0](LICENSE)
