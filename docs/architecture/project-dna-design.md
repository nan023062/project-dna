# Project DNA Current Architecture

> Status: Active
> Last Updated: 2026-04-01
> Scope: current runtime architecture used by the repo

## 1. Product Shape

Project DNA currently uses a **Server + Client desktop host** split.

- `Server` is the shared knowledge service and management console.
- `Client` is the only user-facing local workspace host.
- `Client` is **single-process, single-window, single-lifecycle**.
- The local `:5052` surface is embedded inside the desktop Client process. It exists only to support the desktop host and IDE MCP access.

This is the current MVP direction and the basis for architecture reviews.

## 2. Runtime Topology

```text
Browser
  |
  +--> Project DNA Server (:5051)
  |      - REST API
  |      - Server dashboard (wwwroot)
  |      - Knowledge graph / memory / governance
  |
  +--> Project DNA Client Desktop (single window)
         - project loader
         - topology preview
         - knowledge preview
         - connection status
         - tooling / MCP entry
         - embedded local API + MCP on :5052
                |
                +--> Cursor / Codex / other IDE agents
```

Key boundary:

- IDEs do **not** connect directly to Server MCP.
- IDEs connect to `Client` on `http://127.0.0.1:5052/mcp`.
- The embedded `5052` surface is available only after the desktop Client has loaded a project.

## 3. Layered Dependency Rule

Current dependency chain must remain a DAG:

`Dna.Core <- Dna.Knowledge <- Dna.Server`

Additional UI-facing layers are attached without reversing this chain:

- `Client` depends on shared runtime / API contracts but must not introduce reverse dependencies back into lower layers.
- Shared web UI code should be extracted into dedicated shared modules instead of making Server and Client depend on each other.

## 4. Server Responsibilities

`Server` is the system of record.

Current responsibilities:

- store graph, memory, and governance data
- keep server-scoped runtime model reservation in `<knowledge-store>/llm/server-llm.json`
- expose REST APIs
- host the server dashboard in `wwwroot`
- maintain connection allowlist / role state
- provide admin-facing management pages

Current non-responsibilities:

- does not directly read project source code
- does not act as the user-facing Client desktop shell
- does not own IDE-local lifecycle

## 5. Client Responsibilities

`Client` is the local desktop host.

Current responsibilities:

- open one main desktop window
- load one target project via `.project.dna/project.json`
- keep project-scoped runtime model reservation in `.project.dna/llm.json`
- keep project-scoped workspace state in `.project.dna/client-workspaces.json`
- keep project-scoped local agent shell state in `.project.dna/agent-shell/agent-shell-state.json`
- start and stop the embedded local API / MCP surface in the same process
- preview topology and formal knowledge
- show current Server connection / permission state
- install or expose IDE integration endpoints

Current non-responsibilities:

- not a separate browser workbench
- not a second standalone host mode
- not a team-shared server entry
- not the authority for permission decisions

## 6. Data and Access Boundaries

Current MVP boundary is intentionally narrow:

- `Server` is the source of truth for formal knowledge.
- `Client` mainly previews formal knowledge and exposes local MCP access.
- direct formal knowledge writes are currently limited to `admin`
- non-admin Client paths should not imply unrestricted formal knowledge editing

Historical review / JWT / team-scale flows remain in the repo as future direction, but they are not the primary MVP runtime path.

Runtime model reservation is now split by ownership:

- `Server`: `<knowledge-store>/llm/server-llm.json`
- `Client`: `.project.dna/llm.json`
- `Client` workspace state: `.project.dna/client-workspaces.json`
- `Client` local agent shell state: `.project.dna/agent-shell/agent-shell-state.json`
- existing `~/.dna/config.json` remains a separate user-level global config source

## 7. Embedded Client Surface

The embedded local surface currently includes:

- `/mcp`
- `/api/client/status`
- `/api/client/workspaces/*`
- `/api/client/tooling/*`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/agent/*`

Interpretation:

- this surface exists to serve the desktop host and IDE integrations
- it is not a separate Client web product

## 8. Startup Model

### Server

- fixed default port: `5051`
- current startup requires `--db <directory>`

### Client

- fixed embedded local port: `5052`
- desktop window starts first
- user selects a project containing `.project.dna/project.json`
- after project load succeeds, the embedded local API / MCP surface becomes available

This enforces the product rule:

> one client, one mental model, one lifecycle

## 9. Current UX Entrances

### Server

Primary UX entrance:

- browser -> `http://localhost:5051`

Main page groups:

- service overview
- connection / allowlist management
- review queue foundation
- knowledge / topology management

### Client

Primary UX entrance:

- desktop main window

Main page groups:

- project loader
- overview
- connection permissions
- topology preview
- knowledge preview
- tooling / MCP hub

## 10. Architecture Review Checklist

Future architecture reviews should check these questions first:

1. Does any new change break the single-process, single-window Client model?
2. Does any new module introduce reverse or circular dependencies?
3. Does any new UX imply that Client is again a separate browser workbench?
4. Does any new write path bypass Server ownership of formal knowledge?
5. Are Server admin UX and Client desktop UX still clearly separated?
6. Is shared UI logic extracted into shared libraries instead of copy-pasted?

## 11. Near-Term Direction

The next evolution should continue from the current MVP baseline:

- keep Server focused on management, formal knowledge, and admin operations
- keep Client focused on desktop hosting, project cognition, and local MCP access
- postpone detailed task orchestration / chat workflows until the shared agent orchestration library is introduced
- expand to team-scale auth / review only after the single-user local admin loop is stable
