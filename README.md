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
  - built-in agent orchestration, execution loop, model interaction, tool-call policy
- `Dna.Workbench`
  - unified project capability surface for built-in agents, external agents, CLI, and the desktop host
- `Dna.Knowledge`
  - Workspace, TopoGraph, Memory, Governance

## Workbench vs Agent

This is the key boundary of the current design:

- `Dna.Agent`
  - decides how work is planned and executed
- `Dna.Workbench`
  - decides what project capabilities are available

External agents such as Cursor, Codex, or Claude Code do not need `Dna.Agent` to exist. They already have their own orchestration. What they need is the project-specific capability surface exposed by `Dna.Workbench`.

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

- [ROADMAP.md](ROADMAP.md)
- [src/Dna.Agent/ARCHITECTURE.md](src/Dna.Agent/ARCHITECTURE.md)
- [src/Dna.Workbench/ARCHITECTURE.md](src/Dna.Workbench/ARCHITECTURE.md)
- [src/Dna.Knowledge/ARCHITECTURE.md](src/Dna.Knowledge/ARCHITECTURE.md)

## License

[Apache 2.0](LICENSE)
