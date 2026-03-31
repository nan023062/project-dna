# Project DNA MCP Agent Hooks (Codex)

Use this file as team default Codex prompt guidance.

- MCP Server: project-dna
- MCP Endpoint: {{MCP_ENDPOINT}}

## Mandatory Workflow

1. Session start: validate project identity first when tools exist.
2. Task start: before editing files, call `get_context("module_name")`.
3. During task: call `recall("question")` before uncertain decisions.
4. Important decisions: call `remember()` with `#decision`.
5. Task end: call `remember()` with `#completed-task`; add `#lesson` if needed.
