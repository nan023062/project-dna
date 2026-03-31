using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dna.Client.Services.Tooling;

public sealed class ClientIdeToolingService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ClientToolingTargetStatus GetStatus(
        string target,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName)
    {
        var normalized = NormalizeTarget(target);
        var paths = BuildPaths(normalized, workspaceRoot);

        var filesExist = new[]
        {
            paths.McpFile,
            paths.PromptFile,
            paths.AgentFile
        }.All(File.Exists);

        var mcpConfigured = IsMcpConfigured(paths.McpFile, mcpEndpoint, serverName);

        return new ClientToolingTargetStatus
        {
            Id = normalized,
            DisplayName = normalized == "cursor" ? "Cursor" : "Codex",
            Description = normalized == "cursor"
                ? "Install .cursor/mcp.json + rules + agents."
                : "Install .codex/mcp.json + prompts + agents.",
            Installed = filesExist && mcpConfigured,
            McpConfigured = mcpConfigured,
            Paths = paths
        };
    }

    public ClientToolingInstallReport InstallTarget(
        string target,
        string workspaceRoot,
        string mcpEndpoint,
        string serverName,
        bool replaceExisting)
    {
        var normalized = NormalizeTarget(target);
        var paths = BuildPaths(normalized, workspaceRoot);
        var report = new ClientToolingInstallReport
        {
            Target = normalized,
            DisplayName = normalized == "cursor" ? "Cursor" : "Codex",
            Paths = paths
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.McpFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.PromptFile)!);
            Directory.CreateDirectory(Path.GetDirectoryName(paths.AgentFile)!);

            UpdateMcpConfig(paths.McpFile, serverName, mcpEndpoint, report);

            var promptContent = normalized == "cursor"
                ? BuildCursorRuleContent(mcpEndpoint)
                : BuildCodexPromptContent(mcpEndpoint);
            var agentContent = BuildAgentContent(mcpEndpoint, serverName, normalized);

            WriteManagedFile(paths.PromptFile, promptContent, replaceExisting, report);
            WriteManagedFile(paths.AgentFile, agentContent, replaceExisting, report);
        }
        catch (Exception ex)
        {
            report.Warnings.Add(ex.Message);
        }

        return report;
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

    private static void UpdateMcpConfig(
        string mcpFile,
        string serverName,
        string mcpEndpoint,
        ClientToolingInstallReport report)
    {
        JsonObject root;

        if (File.Exists(mcpFile))
        {
            try
            {
                root = JsonNode.Parse(File.ReadAllText(mcpFile)) as JsonObject ?? new JsonObject();
            }
            catch
            {
                report.Warnings.Add($"Existing mcp.json parse failed, fallback to overwrite: {mcpFile}");
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var mcpServers = root["mcpServers"] as JsonObject;
        if (mcpServers == null)
        {
            mcpServers = new JsonObject();
            root["mcpServers"] = mcpServers;
        }

        mcpServers[serverName] = new JsonObject
        {
            ["url"] = mcpEndpoint
        };

        WriteJsonFile(mcpFile, root, report);
    }

    private static void WriteJsonFile(string path, JsonObject root, ClientToolingInstallReport report)
    {
        var content = JsonSerializer.Serialize(root, JsonOptions);
        if (File.Exists(path))
        {
            var backup = CreateBackup(path);
            if (backup != null) report.BackupFiles.Add(backup);
        }

        File.WriteAllText(path, content);
        report.WrittenFiles.Add(path);
    }

    private static void WriteManagedFile(
        string path,
        string content,
        bool replaceExisting,
        ClientToolingInstallReport report)
    {
        if (File.Exists(path) && !replaceExisting)
        {
            report.SkippedFiles.Add(path);
            return;
        }

        if (File.Exists(path))
        {
            var backup = CreateBackup(path);
            if (backup != null) report.BackupFiles.Add(backup);
        }

        File.WriteAllText(path, content);
        report.WrittenFiles.Add(path);
    }

    private static string? CreateBackup(string path)
    {
        try
        {
            var backupPath = $"{path}.{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            return backupPath;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMcpConfigured(string mcpFile, string endpoint, string serverName)
    {
        if (!File.Exists(mcpFile)) return false;

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(mcpFile)) as JsonObject;
            var mcpServers = root?["mcpServers"] as JsonObject;
            if (mcpServers == null) return false;

            var byName = mcpServers[serverName] as JsonObject;
            var byNameUrl = byName?["url"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(byNameUrl))
            {
                return true;
            }

            foreach (var server in mcpServers)
            {
                var obj = server.Value as JsonObject;
                var url = obj?["url"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url)) continue;
                if (string.Equals(url, endpoint, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string NormalizeTarget(string target)
    {
        var normalized = (target ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "cursor" or "codex") return normalized;
        throw new ArgumentException($"Unsupported target: {target}");
    }

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

    private static string BuildAgentContent(string endpoint, string serverName, string target) =>
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
}

public sealed class ClientToolingTargetPaths
{
    public string McpFile { get; init; } = "";
    public string PromptFile { get; init; } = "";
    public string AgentFile { get; init; } = "";
}

public sealed class ClientToolingTargetStatus
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool Installed { get; init; }
    public bool McpConfigured { get; init; }
    public ClientToolingTargetPaths Paths { get; init; } = new();
}

public sealed class ClientToolingInstallReport
{
    public string Target { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public ClientToolingTargetPaths Paths { get; init; } = new();
    public List<string> WrittenFiles { get; init; } = [];
    public List<string> SkippedFiles { get; init; } = [];
    public List<string> BackupFiles { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
