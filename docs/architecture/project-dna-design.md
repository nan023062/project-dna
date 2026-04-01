# Project DNA Current Architecture

> Status: Active
> Last Updated: 2026-04-01
> Scope: current supported runtime architecture

## 1. Product Shape

Project DNA is currently a **single desktop Client**.

Supported product rules:

- one client
- one process
- one window
- one mental model
- one lifecycle

The embedded runtime on `:5052` exists inside that desktop Client process and is shared by:

- desktop UI
- local CLI
- IDE agents through MCP

Legacy `Server` code may still exist temporarily in the repository, but it is not part of the active documented architecture.

## 2. Runtime Topology

```text
User
  |
  +--> Agentic OS Client Desktop
         - project loader
         - overview
         - knowledge graph
         - knowledge preview
         - local agent shell
         - tooling / MCP entry
         - embedded local runtime on :5052
                |
                +--> Desktop UI support APIs
                +--> dna_client cli
                +--> Cursor / Codex / other IDE agents via /mcp
```

Key boundary:

- there is no separate Client browser workbench
- there is no second long-running service for Client
- there is no supported multi-user runtime in the current product shape

## 3. Layering Rule

For the supported Client-only runtime, the important layering direction is:

```text
Dna.Core
  ->
Dna.Knowledge
  ->
Dna.Web.Shared
  ->
Client desktop host and local runtime
```

Rules:

- lower layers must not depend on Client desktop code
- desktop UX may depend on shared runtime and UI support code
- graph, memory, tooling, and agent-shell features should be extracted into reusable layers before they are re-skinned

## 4. Client Responsibilities

The desktop Client is responsible for:

- opening one main desktop window
- loading one project through `.project.dna/project.json`
- initializing the local project-scoped knowledge runtime
- exposing the embedded local runtime on `http://127.0.0.1:5052`
- previewing topology and knowledge
- exposing MCP to IDE agents
- exposing local CLI-compatible APIs
- managing project-scoped workspace state
- managing project-scoped LLM config reservation
- hosting the lightweight local agent shell

The desktop Client is not:

- a browser-first product
- a second host mode
- a team server
- a remote authority service

## 5. Project-Scoped State

The supported state layout is project-scoped under `.project.dna/`:

- `project.json`
- `llm.json`
- `logs/`
- `client-workspaces.json`
- `agent-shell/agent-shell-state.json`

Current behavior:

- the project identity comes from `.project.dna/project.json`
- the current runtime only requires `projectName`
- legacy fields such as `serverBaseUrl` may remain in old configs, but they are not required by the current Client-only path
- the local knowledge store is initialized from the project-scoped metadata root

User-level global config may still exist under `~/.dna/config.json`, but it is separate from the project-scoped runtime data.

## 6. Embedded Local Runtime

The embedded runtime currently includes:

- `/mcp`
- `/api/status`
- `/api/topology`
- `/api/connection/access`
- `/api/memory/*`
- `/api/client/status`
- `/api/client/workspaces/*`
- `/api/client/tooling/*`
- `/agent/*`

Interpretation:

- `/api/status` and `/api/topology` are the main local runtime read surfaces
- `/api/client/*` supports desktop host state and tooling flows
- `/agent/*` is the lightweight local agent shell surface
- `/mcp` is the IDE agent entry

## 7. Startup Model

Current startup sequence:

1. launch the desktop Client
2. choose a project containing `.project.dna/project.json`
3. initialize project-scoped local runtime data
4. bring local `:5052` runtime online
5. enable desktop UI, CLI, and IDE integrations against the same local runtime

This sequence preserves the core rule:

> one client, one mental model, one lifecycle

## 8. Current UX Entrances

The active UX entrances are:

- desktop main window
- `dna_client cli`
- MCP from Cursor / Codex / other IDE agents

The desktop window currently centers on:

- project loading
- overview
- runtime status
- knowledge graph
- knowledge preview
- tooling / MCP integration

## 9. Current Graph Architecture Direction

The topology subsystem is being split into layered graph parts:

- scene model
- layout engines
- view state
- render list
- renderer backend
- caches and LOD policy
- spatial index and interaction helpers

This work is tracked in:

- [client-topology-upgrade-plan.md](./client-topology-upgrade-plan.md)

## 10. Architecture Review Checklist

Future architecture reviews should check these questions first:

1. Does any change break the single-process, single-window Client model?
2. Does any change introduce a second user-facing runtime mode?
3. Does any new feature require a remote authority when it could remain local?
4. Are project-scoped files still stored under `.project.dna/`?
5. Do desktop UI, CLI, and MCP still converge on the same local `:5052` runtime?
6. Are graph and agent features being extracted into layered modules instead of pushed back into one giant UI file?

## 11. Near-Term Direction

The near-term direction remains:

- keep the product Client-only
- continue improving local topology and knowledge UX
- keep MCP and CLI as first-class integration surfaces
- keep local project cognition and memory management as the main value
- postpone any new team/server design until after the current Client-only architecture is fully settled
