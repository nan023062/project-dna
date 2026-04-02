namespace Dna.App.Services.Tooling;

public sealed class AppToolingTargetCatalog
{
    public AppToolingTargetDefinition Get(string target, string workspaceRoot)
    {
        var normalized = Normalize(target);
        return new AppToolingTargetDefinition
        {
            Id = normalized,
            DisplayName = normalized == "cursor" ? "Cursor" : "Codex",
            Description = normalized == "cursor"
                ? "Install .cursor/mcp.json + rules + agents."
                : "Install .codex/mcp.json + prompts + agents.",
            Paths = BuildPaths(normalized, workspaceRoot)
        };
    }

    public string Normalize(string target)
    {
        var normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "cursor" or "codex")
            return normalized;

        throw new ArgumentException($"Unsupported target: {target}");
    }

    private static AppToolingTargetPaths BuildPaths(string target, string workspaceRoot)
    {
        if (target == "cursor")
        {
            return new AppToolingTargetPaths
            {
                McpFile = Path.Combine(workspaceRoot, ".cursor", "mcp.json"),
                PromptFile = Path.Combine(workspaceRoot, ".cursor", "rules", "agentic-os-mcp-hook.mdc"),
                AgentFile = Path.Combine(workspaceRoot, ".cursor", "agents", "agentic-os-mcp-hooks.md")
            };
        }

        return new AppToolingTargetPaths
        {
            McpFile = Path.Combine(workspaceRoot, ".codex", "mcp.json"),
            PromptFile = Path.Combine(workspaceRoot, ".codex", "prompts", "agentic-os-mcp-hook.md"),
            AgentFile = Path.Combine(workspaceRoot, ".codex", "agents", "agentic-os-mcp-hooks.md")
        };
    }
}

public sealed class AppToolingTargetDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public AppToolingTargetPaths Paths { get; init; } = new();
}
