namespace Dna.Client.Services.Tooling;

public sealed class ClientToolingTargetCatalog
{
    public ClientToolingTargetDefinition Get(string target, string workspaceRoot)
    {
        var normalized = Normalize(target);
        return new ClientToolingTargetDefinition
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

    private static ClientToolingTargetPaths BuildPaths(string target, string workspaceRoot)
    {
        if (target == "cursor")
        {
            return new ClientToolingTargetPaths
            {
                McpFile = Path.Combine(workspaceRoot, ".cursor", "mcp.json"),
                PromptFile = Path.Combine(workspaceRoot, ".cursor", "rules", "project-dna-mcp-hook.mdc"),
                AgentFile = Path.Combine(workspaceRoot, ".cursor", "agents", "project-dna-mcp-hooks.md")
            };
        }

        return new ClientToolingTargetPaths
        {
            McpFile = Path.Combine(workspaceRoot, ".codex", "mcp.json"),
            PromptFile = Path.Combine(workspaceRoot, ".codex", "prompts", "project-dna-mcp-hook.md"),
            AgentFile = Path.Combine(workspaceRoot, ".codex", "agents", "project-dna-mcp-hooks.md")
        };
    }
}

public sealed class ClientToolingTargetDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public ClientToolingTargetPaths Paths { get; init; } = new();
}
