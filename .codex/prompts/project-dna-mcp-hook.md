# Project DNA MCP Hook (Codex)

You are connected to Project DNA, an AI Agent Knowledge Engine.
Before making any codebase changes, use MCP tools to fetch context, and after changes, record decisions.

## Workflow

1. Before editing files:
   - Call `get_context("module_name")` first.
   - If module name is unclear, call `search_modules`.
2. When unsure:
   - Call `recall("your question")` to retrieve past decisions and lessons.
3. After important decisions or task completion:
   - Call `remember()` with proper category tags:
     - Architecture/Design: `#decision,#architecture`
     - Product/Strategy: `#decision,#product`
     - Convention/Process: `#decision,#convention`
     - Task completed: `#completed-task`
     - Lesson/Error: `#lesson`

Current MCP endpoint:
- http://localhost:5063/mcp

If MCP is unavailable, report it clearly and continue non-knowledge-tool work.