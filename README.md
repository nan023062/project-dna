# Project DNA

**The local project knowledge runtime for AI agents.**

Project DNA gives agents project-level cognition: structure, dependencies, constraints, decisions, and lessons learned.

[中文文档](README.zh-CN.md)

## Current Product Shape

Project DNA is currently documented and supported as a **single desktop Client**:

- one process
- one window
- one mental model
- one lifecycle

The embedded local runtime lives inside the desktop Client process on `http://127.0.0.1:5052`.

That local runtime serves three surfaces at the same time:

- desktop UI support APIs
- local CLI
- MCP for Cursor, Codex, and other IDE agents

## Runtime Topology

```text
User
  |
  +--> Agentic OS Client Desktop (single window)
         - project loader
         - topology preview
         - knowledge preview
         - workspace state
         - local agent shell
         - tooling / MCP entry
         - embedded local runtime on :5052
                |
                +--> Desktop UI
                +--> agentic-os cli
                +--> Cursor / Codex / other IDE agents via /mcp
```

## Quick Start

### 1. Build

```bash
dotnet build src/Client/Client.csproj
```

### 2. Start the Desktop Client

Development:

```bash
dotnet run --no-launch-profile --project src/Client
```

Published executable:

```bash
publish/agentic-os.exe
```

### 3. Prepare a Project

The desktop Client loads a target project folder that contains:

```text
.agentic-os/project.json
```

Minimal example:

```json
{
  "projectName": "agentic-os"
}
```

Notes:

- the legacy `serverBaseUrl` field may still appear in old files, but the current Client-only runtime does not require it
- the Client opens its local runtime only after the project is loaded

### 4. Connect IDE Agents

After the desktop Client has loaded a project, point your IDE MCP config to:

```json
{
  "mcpServers": {
    "project-dna": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

## Project-Scoped State

The Client stores project-scoped state under `.agentic-os/`:

- `project.json`: project identity
- `llm.json`: Client runtime LLM config reservation
- `logs/`: Client logs
- `client-workspaces.json`: workspace state
- `agent-shell/agent-shell-state.json`: local agent shell state

The current local knowledge store is also initialized from the project-scoped metadata root.

## Client Runtime Surface

The embedded local runtime currently exposes:

- `/mcp`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/api/client/status`
- `/api/client/workspaces/*`
- `/api/client/tooling/*`
- `/agent/*`

This surface exists to support the desktop host, CLI, and IDE integrations. It is not a separate browser product.

## CLI

The desktop Client ships with a local CLI entry:

```bash
agentic-os cli status
agentic-os cli topology
agentic-os cli search render
agentic-os cli recall "what constraints apply"
agentic-os cli memories
agentic-os cli tools
```

Default local runtime address:

```text
http://127.0.0.1:5052
```

## MCP Tools

### Graph Tools

| Tool | Description |
|------|-------------|
| `get_topology` | Full knowledge graph overview |
| `get_context` | Get module context with constraints, dependencies, and lessons |
| `search_modules` | Search nodes by keyword |
| `get_dependency_order` | Topological sort for multi-module changes |
| `register_module` | Add or update a knowledge node |
| `register_crosswork` | Declare cross-module collaboration |
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

- the supported runtime is **Client-only**
- the embedded runtime is local to the desktop process
- desktop UI, CLI, and MCP all converge on the same local `:5052` surface
- topology work is currently being split into scene, layout, render, cache, and LOD layers

See:

- [docs/architecture/project-dna-design.md](docs/architecture/project-dna-design.md)
- [docs/architecture/project-dna-transport-auth-decision.md](docs/architecture/project-dna-transport-auth-decision.md)
- [docs/architecture/client-topology-upgrade-plan.md](docs/architecture/client-topology-upgrade-plan.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[Apache 2.0](LICENSE)
