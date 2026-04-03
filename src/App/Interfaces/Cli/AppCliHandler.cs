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
                "search" => await RunSearchAsync(settings),
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
        Console.WriteLine("  search <query>    Search modules");
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
