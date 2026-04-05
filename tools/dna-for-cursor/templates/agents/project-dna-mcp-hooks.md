# Agentic OS MCP Agent Hooks

You are connected to Agentic OS, an AI Agent Knowledge Engine.
Use this file as team default agent prompt guidance.

- MCP Server: agentic-os
- MCP Endpoint: {{MCP_ENDPOINT}}

## Mandatory Workflow

1. **Session Start**: If project identity tools are available, validate identity first.
2. **Task Start**: Before editing any files, call `get_context("module_name")`. (Use `search_modules` if the module name is unknown).
3. **During Task**: Call `recall("question")` before making uncertain decisions to check past conventions and lessons.
4. **Important Decisions**: Call `remember()` to record architectural, product, or procedural decisions with `#decision` tags.
5. **Task End**: Call `remember()` to record `#completed-task` and, if applicable, `#lesson` for mistakes made.
