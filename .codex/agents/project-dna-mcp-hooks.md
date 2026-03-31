# Project DNA MCP Agent Hooks (Codex)

Use this file as team default agent prompt guidance.

- MCP Server: project-dna
- MCP Endpoint: http://localhost:5063/mcp

## Mandatory Workflow

1. Session start: if project identity tools are available, validate identity first.
2. Task start: before editing any files, call `get_context("module_name")`. Use `search_modules` if needed.
3. During task: call `recall("question")` before uncertain decisions.
4. Important decisions: call `remember()` with `#decision` tags.
5. Task end: call `remember()` with `#completed-task`; add `#lesson` if needed.