namespace Dna.App.Services.Tooling;

public sealed class AppToolingContentBuilder
{
    public string BuildPromptContent(string target, string endpoint)
        => target == "cursor"
            ? BuildCursorRuleContent(endpoint)
            : BuildCodexPromptContent(endpoint);

    public string BuildAgentContent(string endpoint, string serverName, string target) =>
        $$"""
        # Project DNA MCP Agent Hooks ({{(target == "cursor" ? "Cursor" : "Codex")}})

        Use this file as team default agent prompt guidance.

        - MCP Server: {{serverName}}
        - MCP Endpoint: {{endpoint}}

        ## Mandatory Workflow

        1. Session start: if project identity tools are available, validate identity first.
        2. Task start: before editing any files, call `get_context("module_name")`. Use `search_modules` if needed.
        3. During task: call `recall("question")` before uncertain decisions.
        4. Important decisions: call `remember()` with `#decision` tags.
        5. Task end: call `remember()` with `#completed-task`; add `#lesson` if needed.
        """;

    private static string BuildCursorRuleContent(string endpoint) =>
        """
        ---
        description: Project DNA MCP Hook gate
        globs: ["**/*"]
        alwaysApply: true
        ---

        # Project DNA MCP Hook

        You are connected to Project DNA, an AI Agent Knowledge Engine.
        Before making any codebase changes, you MUST use the MCP tools to fetch context and after changes, you MUST record decisions.

        ## Workflow

        1. **Before Editing Files**:
           - Call `get_context("module_name")` to fetch architecture and constraints. If unsure about the module name, call `search_modules` first.
        2. **When in Doubt**:
           - Call `recall("your question")` to search the knowledge base for past decisions, conventions, or lessons.
        3. **After Making Decisions or Completing Tasks**:
           - Call `remember()` to store the knowledge. Categorize it properly:
             - Architecture/Design -> `type="Structural", layer="DisciplineStandard", tags="#decision,#architecture"`
             - Product/Strategy -> `type="Structural", layer="ProjectVision", tags="#decision,#product"`
             - Conventions/Process -> `type="Procedural", layer="DisciplineStandard", tags="#decision,#convention"`
             - Task Completed -> `type="Episodic", layer="Implementation", tags="#completed-task"`
             - Lessons/Errors -> `type="Episodic", layer="Implementation", tags="#lesson"`

        Current MCP endpoint:
        - {{MCP_ENDPOINT}}

        If MCP is unavailable, report it clearly and continue normal work.
        """
        .Replace("{{MCP_ENDPOINT}}", endpoint, StringComparison.Ordinal);

    private static string BuildCodexPromptContent(string endpoint) =>
        """
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
        - {{MCP_ENDPOINT}}

        If MCP is unavailable, report it clearly and continue non-knowledge-tool work.
        """
        .Replace("{{MCP_ENDPOINT}}", endpoint, StringComparison.Ordinal);
}
