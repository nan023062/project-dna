# Agentic OS

**The local project knowledge runtime for AI agents.**

Agentic OS gives agents project-level cognition: structure, dependencies, constraints, design decisions, and lessons learned.

[中文文档](README.zh-CN.md)

## Current Product Shape

This repository currently supports a **single desktop App** only:

- one process
- one window
- one mental model
- one lifecycle

The App still hosts a local runtime on `http://127.0.0.1:5052`, but that HTTP surface is increasingly treated as an external and compatibility layer rather than the preferred in-process desktop path.

## Architecture Direction

### Current implementation

```text
App
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

### Target architecture

```text
App
  ->
Dna.Agent
  ->
Dna.Workbench
  ->
Dna.Knowledge
  ->
Dna.Core
```

Responsibilities:

- `Dna.Agent`
  - built-in agent orchestration, requirement planning, execution loop, model interaction, tool-call policy
- `Dna.Workbench`
  - unified project capability surface, task bridge, and governance bridge for built-in agents, external agents, CLI, and the desktop host
- `Dna.Knowledge`
  - Workspace, TopoGraph, Memory, Governance

## Workbench vs Agent

This is the key boundary of the current design:

- `Dna.Agent`
  - decides how work is decomposed, planned, and executed
- `Dna.Workbench`
  - decides what project capabilities are available, which module a task may target, and what isolated context it may see

External agents such as Cursor, Codex, or Claude Code do not need `Dna.Agent` to exist. They already have their own orchestration. What they need is the project-specific capability surface exposed by `Dna.Workbench`.

## Standard Task Flow

Every agent should follow the same loop:

1. ask `Dna.Workbench` to resolve the requirement against `TopoGraph + MCDP`
2. create multiple single-module tasks from that result
3. call `startTask` for one task and receive the isolated task context
4. execute inside that context with tools and model capabilities
5. call `endTask` with outcome, decisions, lessons, and blockers
6. continue the remaining task chain serially or in parallel when modules do not conflict

The core rule is:

- one task
- one target module
- one isolated operation scope
- one required `endTask`

## Governance Flow

Besides the normal task loop, `Dna.Workbench` also supports a governance loop:

1. an agent sends a governance request for the whole graph or a selected module scope
2. `Dna.Workbench` returns the matching module tree as governance context
3. the agent decomposes that scope into multiple governance single-task sessions
4. those governance tasks still use the same `startTask / endTask` lifecycle

This means governance is not a bulk opaque operation. It still falls back to single-module tasks with the same isolation and conflict rules.

## Quick Start

### 1. Build

```bash
dotnet build src/App/App.csproj
```

### 2. Start the desktop App

```bash
dotnet run --no-launch-profile --project src/App
```

Published executable:

```bash
publish/agentic-os.exe
```

### 3. Connect IDE agents

After the App has loaded a project, point your MCP config to:

```json
{
  "mcpServers": {
    "agentic-os": {
      "url": "http://localhost:5052/mcp"
    }
  }
}
```

## Documentation

- [src/Dna.Agent/ARCHITECTURE.md](src/Dna.Agent/ARCHITECTURE.md)
- [src/Dna.Workbench/ARCHITECTURE.md](src/Dna.Workbench/ARCHITECTURE.md)
- [src/Dna.Knowledge/ARCHITECTURE.md](src/Dna.Knowledge/ARCHITECTURE.md)

## License

[Apache 2.0](LICENSE)
