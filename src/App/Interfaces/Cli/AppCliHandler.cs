using System.Text;
using System.Text.Json;
using Dna.App.Services;

namespace Dna.App.Interfaces.Cli;

internal static class AppCliHandler
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var settings = AppCliSettings.Parse(args);
        var command = settings.Args.Count > 0 ? settings.Args[0].ToLowerInvariant() : "help";

        try
        {
            return command switch
            {
                "status" => await RunStatusAsync(settings),
                "topology" or "topo" => await RunTopologyAsync(settings),
                "plan" => await RunPlanAsync(settings),
                "mcdp" => await RunMcdpAsync(settings),
                "search" => await RunSearchAsync(settings),
                "context" => await RunContextAsync(settings),
                "session" => await RunSessionAsync(settings),
                "recall" => await RunRecallAsync(settings),
                "memory" or "memories" => await RunMemoriesAsync(settings),
                "tools" or "mcp" => await RunToolsAsync(settings),
                "help" or "--help" or "-h" => RunHelp(settings.BaseUrl),
                _ => RunUnknown(command, settings.BaseUrl)
            };
        }
        catch (HttpRequestException)
        {
            WriteError($"Cannot connect to local app runtime: {settings.BaseUrl}");
            Console.WriteLine("  Start the desktop app first so the local runtime is available.");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunStatusAsync(AppCliSettings settings)
    {
        var runtime = await GetJsonAsync(settings.BaseUrl, "/api/status");
        var app = await GetJsonAsync(settings.BaseUrl, "/api/app/status");

        WriteHeader("Agentic OS Runtime");
        Console.WriteLine($"  URL:            {settings.BaseUrl}");
        Console.WriteLine($"  Project:        {GetString(runtime, "projectName", "-")}");
        Console.WriteLine($"  Project root:   {GetString(runtime, "projectRoot", "-")}");
        Console.WriteLine($"  Metadata root:  {GetString(runtime, "storePath", "-")}");
        Console.WriteLine($"  Memory root:    {GetString(runtime, "memoryStorePath", "-")}");
        Console.WriteLine($"  Session root:   {GetString(runtime, "sessionStorePath", "-")}");
        Console.WriteLine($"  Knowledge root: {GetString(runtime, "knowledgeStorePath", "-")}");
        Console.WriteLine($"  Transport:      {GetString(runtime, "transport", "Local REST + MCP")}");
        Console.WriteLine($"  MCP:            {settings.BaseUrl}/mcp");
        Console.WriteLine($"  Modules:        {GetInt(runtime, "moduleCount")}");
        Console.WriteLine($"  Memories:       {GetInt(runtime, "memoryCount")}");
        Console.WriteLine($"  Sessions:       {GetInt(runtime, "sessionCount")}");
        Console.WriteLine($"  Uptime:         {GetString(runtime, "uptime", "-")}");

        if (app.TryGetProperty("currentWorkspace", out var workspace))
        {
            Console.WriteLine($"  Workspace:      {GetString(workspace, "name", "-")}");
            Console.WriteLine($"  Workspace root: {GetString(workspace, "workspaceRoot", "-")}");
        }

        return 0;
    }

    private static async Task<int> RunTopologyAsync(AppCliSettings settings)
    {
        var topology = await GetJsonAsync(settings.BaseUrl, "/api/topology");
        WriteHeader("Topology");
        Console.WriteLine($"  {GetString(topology, "summary", "Topology loaded.")}");
        Console.WriteLine();

        if (!topology.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  No modules available.");
            return 0;
        }

        foreach (var module in modules.EnumerateArray().Take(20))
        {
            var name = GetString(module, "displayName", GetString(module, "name", "-"));
            var discipline = GetString(module, "disciplineDisplayName", GetString(module, "discipline", "root"));
            var type = GetString(module, "typeLabel", GetString(module, "type", "Module"));
            var parent = GetString(module, "parentModuleId", null);
            var line = new StringBuilder($"  - {name} [{type}] ({discipline})");
            if (!string.IsNullOrWhiteSpace(parent))
                line.Append($" <- {parent}");
            Console.WriteLine(line.ToString());
        }

        var count = modules.GetArrayLength();
        if (count > 20)
            Console.WriteLine($"  ... {count - 20} more modules are available via API or UI.");

        return 0;
    }

    private static async Task<int> RunPlanAsync(AppCliSettings settings)
    {
        var modules = settings.Args.Count > 1
            ? SplitModules(settings.Args.Skip(1))
            : [];
        if (modules.Count == 0)
        {
            WriteError("Usage: agentic-os cli plan <moduleA,moduleB,...>");
            return 1;
        }

        var joined = string.Join(",", modules);
        var json = await GetJsonAsync(settings.BaseUrl, $"/api/plan?modules={Uri.EscapeDataString(joined)}");

        WriteHeader($"Dependency Plan: {joined}");
        Console.WriteLine($"  Order:  {GetString(json, "executionOrder", "(empty)")}");

        var hasCycle = json.TryGetProperty("hasCycle", out var cycleFlag) && cycleFlag.ValueKind == JsonValueKind.True;
        if (hasCycle)
            Console.WriteLine($"  Cycle:  {GetString(json, "cycleDescription", "Detected")}");

        return 0;
    }

    private static async Task<int> RunMcdpAsync(AppCliSettings settings)
    {
        var json = await GetJsonAsync(settings.BaseUrl, "/api/mcdp");
        WriteHeader("MCDP Projection");
        Console.WriteLine($"  Protocol: {GetString(json, "protocolVersion", "1.0")}");
        Console.WriteLine($"  Project:  {GetString(json, "projectName", "-")}");
        Console.WriteLine($"  Root:     {GetString(json, "projectRoot", "-")}");

        if (!json.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  No modules returned.");
            return 0;
        }

        Console.WriteLine($"  Modules:  {modules.GetArrayLength()}");
        foreach (var module in modules.EnumerateArray().Take(10))
        {
            var uid = GetString(module, "uid", "-");
            var type = GetString(module, "type", "-");
            var score = GetInt(module, "layerScore");
            Console.WriteLine($"  - {uid} [{type}] score={score}");
        }

        if (modules.GetArrayLength() > 10)
            Console.WriteLine($"  ... {modules.GetArrayLength() - 10} more modules are available.");

        return 0;
    }

    private static async Task<int> RunContextAsync(AppCliSettings settings)
    {
        var modules = SplitModules(settings.Args.Skip(1));
        if (modules.Count == 0)
        {
            var quickView = await PostJsonAsync(settings.BaseUrl, "/api/graph/begin-task", new { });
            WriteHeader("Module Quick View");
            Console.WriteLine($"  Modules:    {GetInt(quickView, "moduleCount")}");
            Console.WriteLine($"  Edges:      {GetInt(quickView, "edgeCount")}");
            Console.WriteLine($"  CrossWorks: {GetInt(quickView, "crossWorkCount")}");

            if (quickView.TryGetProperty("modules", out var quickModules) && quickModules.ValueKind == JsonValueKind.Array)
            {
                foreach (var module in quickModules.EnumerateArray().Take(12))
                    Console.WriteLine($"  - {GetString(module, "name", "-")} ({GetString(module, "discipline", "generic")})");
            }

            return 0;
        }

        if (modules.Count == 1)
        {
            var target = modules[0];
            var encodedTarget = Uri.EscapeDataString(target);
            var encodedCurrent = Uri.EscapeDataString(target);
            var json = await GetJsonAsync(settings.BaseUrl, $"/api/graph/context?target={encodedTarget}&current={encodedCurrent}");

            WriteHeader($"Context: {target}");

            if (json.TryGetProperty("context", out var context))
            {
                Console.WriteLine($"  Summary:   {GetString(context, "summary", "-")}");
                Console.WriteLine($"  Boundary:  {GetString(context, "boundary", "-")}");
                PrintArray("Constraints", context, "constraints");
            }

            if (json.TryGetProperty("session", out var session))
            {
                Console.WriteLine($"  Session:   {GetInt(session, "count")} item(s)");
                if (session.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray().Take(5))
                    {
                        var summary = GetString(item, "summary", GetString(item, "content", "(empty)"));
                        Console.WriteLine($"    - {summary}");
                    }
                }
            }

            return 0;
        }

        var task = await PostJsonAsync(settings.BaseUrl, "/api/graph/begin-task", new { moduleNames = modules });
        WriteHeader($"Task Context: {string.Join(", ", modules)}");

        if (task.TryGetProperty("contexts", out var contexts) && contexts.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in contexts.EnumerateArray())
            {
                Console.WriteLine($"  - {GetString(item, "module", "-")}");
                if (item.TryGetProperty("context", out var context))
                {
                    Console.WriteLine($"    Summary:  {GetString(context, "summary", "-")}");
                    Console.WriteLine($"    Boundary: {GetString(context, "boundary", "-")}");
                }

                if (item.TryGetProperty("session", out var session))
                    Console.WriteLine($"    Session:  {GetInt(session, "count")} item(s)");
            }
        }

        if (task.TryGetProperty("crossWorks", out var crossWorks) && crossWorks.ValueKind == JsonValueKind.Array)
            Console.WriteLine($"  CrossWorks: {crossWorks.GetArrayLength()}");

        return 0;
    }

    private static async Task<int> RunSessionAsync(AppCliSettings settings)
    {
        var nodeId = settings.Args.Count > 1 ? settings.Args[1].Trim() : null;
        var path = string.IsNullOrWhiteSpace(nodeId)
            ? "/api/session?limit=10"
            : $"/api/session?nodeId={Uri.EscapeDataString(nodeId)}&limit=10";
        var json = await GetJsonAsync(settings.BaseUrl, path);

        WriteHeader(string.IsNullOrWhiteSpace(nodeId) ? "Session" : $"Session: {nodeId}");
        Console.WriteLine($"  Count: {GetInt(json, "count")}");

        if (json.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                index++;
                var summary = GetString(item, "summary", GetString(item, "content", "(empty)"));
                var category = GetString(item, "category", "-");
                Console.WriteLine($"  {index}. ({category}) {summary}");
            }

            if (index == 0)
                Console.WriteLine("  No active session items.");
        }

        return 0;
    }

    private static async Task<int> RunSearchAsync(AppCliSettings settings)
    {
        var query = settings.Args.Count > 1 ? string.Join(' ', settings.Args.Skip(1)) : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            WriteError("Usage: agentic-os cli search <query>");
            return 1;
        }

        var json = await GetJsonAsync(settings.BaseUrl, $"/api/graph/search?q={Uri.EscapeDataString(query)}&maxResults=10");
        WriteHeader($"Module Search: {query}");

        if (json.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var result in results.EnumerateArray())
            {
                var name = GetString(result, "name", "-");
                var discipline = GetString(result, "discipline", "root");
                var summary = GetString(result, "summary", string.Empty);
                Console.WriteLine($"  - {name} ({discipline}) {summary}".TrimEnd());
            }

            if (results.GetArrayLength() == 0)
                Console.WriteLine("  No matching modules found.");
        }

        return 0;
    }

    private static async Task<int> RunRecallAsync(AppCliSettings settings)
    {
        var question = settings.Args.Count > 1 ? string.Join(' ', settings.Args.Skip(1)) : null;
        if (string.IsNullOrWhiteSpace(question))
        {
            WriteError("Usage: agentic-os cli recall <question>");
            return 1;
        }

        var json = await PostJsonAsync(settings.BaseUrl, "/api/memory/recall", new
        {
            question,
            maxResults = 5
        });

        WriteHeader($"Recall: {question}");
        if (!json.TryGetProperty("memories", out var memories) || memories.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  No recall results were returned.");
            return 0;
        }

        var index = 0;
        foreach (var memory in memories.EnumerateArray())
        {
            index++;
            var entry = memory.TryGetProperty("entry", out var nested) ? nested : memory;
            var summary = GetString(entry, "summary", GetString(entry, "content", "(empty)"));
            var stage = GetString(entry, "stage", "-");
            Console.WriteLine($"  {index}. ({stage}) {summary}");
        }

        if (index == 0)
            Console.WriteLine("  No relevant memories found.");

        return 0;
    }

    private static async Task<int> RunMemoriesAsync(AppCliSettings settings)
    {
        var json = await GetJsonAsync(settings.BaseUrl, "/api/memory/query?limit=10&offset=0");
        WriteHeader("Recent Memories");

        if (json.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  No memories available.");
            return 0;
        }

        var index = 0;
        foreach (var memory in json.EnumerateArray())
        {
            index++;
            var id = GetString(memory, "id", "-");
            var stage = GetString(memory, "stage", "-");
            var summary = GetString(memory, "summary", GetString(memory, "content", "(empty)"));
            Console.WriteLine($"  {index}. [{id}] ({stage}) {summary}");
        }

        if (index == 0)
            Console.WriteLine("  Memory store is empty.");

        return 0;
    }

    private static async Task<int> RunToolsAsync(AppCliSettings settings)
    {
        var json = await GetJsonAsync(settings.BaseUrl, "/api/app/mcp/tools");
        WriteHeader("MCP Tools");
        Console.WriteLine($"  MCP endpoint: {GetString(json, "mcpEndpoint", $"{settings.BaseUrl}/mcp")}");

        if (!json.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  No tools returned.");
            return 0;
        }

        foreach (var tool in tools.EnumerateArray())
        {
            var name = GetString(tool, "name", "-");
            var description = GetString(tool, "description", string.Empty);
            Console.WriteLine($"  - {name}: {description}".TrimEnd());
        }

        return 0;
    }

    private static int RunHelp(string baseUrl)
    {
        Console.WriteLine();
        Console.WriteLine("Agentic OS App CLI");
        Console.WriteLine();
        Console.WriteLine($"Usage: agentic-os cli [--url {AppRuntimeConstants.ApiBaseUrl}] <command> [args...]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  status            Show runtime, project, and storage status");
        Console.WriteLine("  topology          Show topology summary");
        Console.WriteLine("  plan <modules>    Show dependency execution order");
        Console.WriteLine("  mcdp              Show MCDP projection summary");
        Console.WriteLine("  search <query>    Search modules");
        Console.WriteLine("  context [mods]    Show quick view, single-module context, or multi-module task context");
        Console.WriteLine("  session [nodeId]  Show short-term session items");
        Console.WriteLine("  recall <question> Recall memories");
        Console.WriteLine("  memories          Show recent memories");
        Console.WriteLine("  tools             Show MCP tool list");
        Console.WriteLine("  help              Show help");
        Console.WriteLine();
        Console.WriteLine($"Default URL: {baseUrl}");
        Console.WriteLine("Environment: DNA_CLIENT_URL or DNA_URL");
        Console.WriteLine();
        return 0;
    }

    private static int RunUnknown(string command, string baseUrl)
    {
        WriteError($"Unknown command: {command}");
        Console.WriteLine("  Run `agentic-os cli help` to see available commands.");
        Console.WriteLine($"  Current default URL: {baseUrl}");
        return 1;
    }

    private static async Task<JsonElement> GetJsonAsync(string baseUrl, string path)
    {
        using var response = await Http.GetAsync(BuildUri(baseUrl, path));
        return await ReadJsonAsync(response);
    }

    private static async Task<JsonElement> PostJsonAsync(string baseUrl, string path, object payload)
    {
        using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(BuildUri(baseUrl, path), content);
        return await ReadJsonAsync(response);
    }

    private static string BuildUri(string baseUrl, string path)
        => new Uri(new Uri($"{baseUrl.TrimEnd('/')}/"), path.TrimStart('/')).ToString();

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var detail = string.IsNullOrWhiteSpace(payload) ? response.ReasonPhrase : payload;
            throw new InvalidOperationException($"{(int)response.StatusCode} {detail}");
        }

        if (string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}").RootElement.Clone();

        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName, string? fallback)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static int GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        return int.TryParse(value.ToString(), out number) ? number : 0;
    }

    private static void PrintArray(string label, JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return;

        var items = value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();

        if (items.Count == 0)
            return;

        Console.WriteLine($"  {label}:");
        foreach (var item in items)
            Console.WriteLine($"    - {item}");
    }

    private static List<string> SplitModules(IEnumerable<string> args)
    {
        return args
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void WriteHeader(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', title.Length));
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error: {message}");
        Console.ResetColor();
    }
}

internal sealed class AppCliSettings
{
    public string BaseUrl { get; init; } = AppRuntimeConstants.ApiBaseUrl;
    public IReadOnlyList<string> Args { get; init; } = [];

    public static AppCliSettings Parse(string[] args)
    {
        var envUrl = Environment.GetEnvironmentVariable("DNA_CLIENT_URL");
        if (string.IsNullOrWhiteSpace(envUrl))
            envUrl = Environment.GetEnvironmentVariable("DNA_URL");

        var baseUrl = string.IsNullOrWhiteSpace(envUrl)
            ? AppRuntimeConstants.ApiBaseUrl
            : AppBootstrap.NormalizeUrl(envUrl);

        var filtered = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                baseUrl = AppBootstrap.NormalizeUrl(args[++i]);
                continue;
            }

            filtered.Add(args[i]);
        }

        return new AppCliSettings
        {
            BaseUrl = baseUrl,
            Args = filtered
        };
    }
}
