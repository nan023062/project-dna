# Project DNA MCP Agent Hooks

Use this file as team default agent prompt guidance.

Always run MCP knowledge workflow before editing files.

- MCP Server: project-dna
- MCP Endpoint: {{MCP_ENDPOINT}}

Dialog hooks:
1. Session start: validate project identity first when tools exist.
2. Task start: run begin_task/get_context (search modules if unknown).
3. During task: call recall before uncertain decisions.
4. Important decisions: remember with #decision.
5. Task end: remember with #completed-task and #lesson when needed.
