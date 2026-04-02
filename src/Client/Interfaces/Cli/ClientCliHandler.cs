using System.Text;
using System.Text.Json;
using Dna.Client.Services;

namespace Dna.Client.Interfaces.Cli;

internal static class ClientCliHandler
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        var settings = ClientCliSettings.Parse(args);
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
            WriteError($"无法连接到本地 Client 运行时：{settings.BaseUrl}");
            Console.WriteLine("  请先启动桌面客户端，让本地 5052 运行时就绪。");
            return 1;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static async Task<int> RunStatusAsync(ClientCliSettings settings)
    {
        var runtime = await GetJsonAsync(settings.BaseUrl, "/api/status");
        var client = await GetJsonAsync(settings.BaseUrl, "/api/client/status");

        WriteHeader("Agentic OS Client Runtime");
        Console.WriteLine($"  地址:         {settings.BaseUrl}");
        Console.WriteLine($"  项目:         {GetString(runtime, "projectName", "-")}");
        Console.WriteLine($"  项目根目录:   {GetString(runtime, "projectRoot", "-")}");
        Console.WriteLine($"  存储目录:     {GetString(runtime, "storePath", "-")}");
        Console.WriteLine($"  传输方式:     {GetString(runtime, "transport", "Local REST + MCP")}");
        Console.WriteLine($"  MCP:          {settings.BaseUrl}/mcp");
        Console.WriteLine($"  模块数:       {GetInt(runtime, "moduleCount")}");
        Console.WriteLine($"  记忆数:       {GetInt(runtime, "memoryCount")}");
        Console.WriteLine($"  运行时长:     {GetString(runtime, "uptime", "-")}");

        if (client.TryGetProperty("currentWorkspace", out var workspace))
        {
            Console.WriteLine($"  当前工作区:   {GetString(workspace, "name", "-")}");
            Console.WriteLine($"  工作区目录:   {GetString(workspace, "workspaceRoot", "-")}");
        }

        return 0;
    }

    private static async Task<int> RunTopologyAsync(ClientCliSettings settings)
    {
        var topology = await GetJsonAsync(settings.BaseUrl, "/api/topology");
        WriteHeader("知识图谱");
        Console.WriteLine($"  {GetString(topology, "summary", "图谱已加载")}");
        Console.WriteLine();

        if (!topology.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  当前没有可显示的模块。");
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
            Console.WriteLine($"  ... 其余 {count - 20} 个模块可在桌面图谱或 API 中查看。");

        return 0;
    }

    private static async Task<int> RunSearchAsync(ClientCliSettings settings)
    {
        var query = settings.Args.Count > 1 ? string.Join(' ', settings.Args.Skip(1)) : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            WriteError("用法: agentic-os cli search <关键字>");
            return 1;
        }

        var json = await GetJsonAsync(settings.BaseUrl, $"/api/graph/search?q={Uri.EscapeDataString(query)}&maxResults=10");
        WriteHeader($"模块搜索: {query}");

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
                Console.WriteLine("  没有找到匹配模块。");
        }

        return 0;
    }

    private static async Task<int> RunRecallAsync(ClientCliSettings settings)
    {
        var question = settings.Args.Count > 1 ? string.Join(' ', settings.Args.Skip(1)) : null;
        if (string.IsNullOrWhiteSpace(question))
        {
            WriteError("用法: agentic-os cli recall <问题>");
            return 1;
        }

        var json = await PostJsonAsync(settings.BaseUrl, "/api/memory/recall", new
        {
            question,
            maxResults = 5
        });

        WriteHeader($"记忆检索: {question}");
        if (!json.TryGetProperty("memories", out var memories) || memories.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  没有返回记忆结果。");
            return 0;
        }

        var index = 0;
        foreach (var memory in memories.EnumerateArray())
        {
            index++;
            var summary = GetString(memory, "summary", GetString(memory, "content", "(空内容)"));
            Console.WriteLine($"  {index}. {summary}");
        }

        if (index == 0)
            Console.WriteLine("  没有找到相关记忆。");

        return 0;
    }

    private static async Task<int> RunMemoriesAsync(ClientCliSettings settings)
    {
        var json = await GetJsonAsync(settings.BaseUrl, "/api/memory/query?limit=10&offset=0");
        WriteHeader("最近记忆");

        if (json.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  没有可显示的记忆。");
            return 0;
        }

        var index = 0;
        foreach (var memory in json.EnumerateArray())
        {
            index++;
            var id = GetString(memory, "id", "-");
            var summary = GetString(memory, "summary", GetString(memory, "content", "(空内容)"));
            Console.WriteLine($"  {index}. [{id}] {summary}");
        }

        if (index == 0)
            Console.WriteLine("  当前记忆库为空。");

        return 0;
    }

    private static async Task<int> RunToolsAsync(ClientCliSettings settings)
    {
        var json = await GetJsonAsync(settings.BaseUrl, "/api/client/mcp/tools");
        WriteHeader("MCP 工具清单");
        Console.WriteLine($"  MCP 入口: {GetString(json, "mcpEndpoint", $"{settings.BaseUrl}/mcp")}");

        if (!json.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Array)
        {
            Console.WriteLine("  没有返回工具清单。");
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
        Console.WriteLine("Agentic OS Client CLI");
        Console.WriteLine();
        Console.WriteLine($"用法: agentic-os cli [--url {ClientRuntimeConstants.ApiBaseUrl}] <命令> [参数...]");
        Console.WriteLine();
        Console.WriteLine("命令:");
        Console.WriteLine("  status            查看本地运行时、当前项目和工作区状态");
        Console.WriteLine("  topology          查看知识图谱摘要");
        Console.WriteLine("  search <关键词>   搜索模块");
        Console.WriteLine("  recall <问题>     检索记忆");
        Console.WriteLine("  memories          查看最近记忆");
        Console.WriteLine("  tools             查看 MCP 工具清单");
        Console.WriteLine("  help              显示帮助");
        Console.WriteLine();
        Console.WriteLine($"默认地址: {baseUrl}");
        Console.WriteLine("环境变量: DNA_CLIENT_URL 或 DNA_URL");
        Console.WriteLine();
        return 0;
    }

    private static int RunUnknown(string command, string baseUrl)
    {
        WriteError($"未知命令: {command}");
        Console.WriteLine("  运行 `agentic-os cli help` 查看可用命令。");
        Console.WriteLine($"  当前默认地址: {baseUrl}");
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

internal sealed class ClientCliSettings
{
    public string BaseUrl { get; init; } = ClientRuntimeConstants.ApiBaseUrl;
    public IReadOnlyList<string> Args { get; init; } = [];

    public static ClientCliSettings Parse(string[] args)
    {
        var envUrl = Environment.GetEnvironmentVariable("DNA_CLIENT_URL");
        if (string.IsNullOrWhiteSpace(envUrl))
            envUrl = Environment.GetEnvironmentVariable("DNA_URL");

        var baseUrl = string.IsNullOrWhiteSpace(envUrl)
            ? ClientRuntimeConstants.ApiBaseUrl
            : ClientBootstrap.NormalizeUrl(envUrl);

        var filtered = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                baseUrl = ClientBootstrap.NormalizeUrl(args[++i]);
                continue;
            }

            filtered.Add(args[i]);
        }

        return new ClientCliSettings
        {
            BaseUrl = baseUrl,
            Args = filtered
        };
    }
}
