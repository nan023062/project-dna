using System.Text.Json;

namespace Dna.Interfaces.Cli;

/// <summary>
/// CLI 轻客户端：当前仅保留状态与拓扑查询。
/// </summary>
public static class CliHandler
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string _baseUrl = "http://localhost:5051";

    public static async Task<int> RunAsync(string[] args)
    {
        var filtered = ParseGlobalOptions(args);
        var subCommand = filtered.Count > 1 ? filtered[1].ToLowerInvariant() : "help";

        try
        {
            return subCommand switch
            {
                "status" => await RunStatus(),
                "topology" or "topo" => await RunTopology(),
                "validate" => await RunValidate(),
                "search" => await RunSearch(filtered),
                "recall" => await RunRecall(filtered),
                "stats" => await RunStats(),
                "export" => await RunExport(),
                "import" => await RunImport(),
                "help" or "--help" or "-h" => RunHelp(),
                _ => RunUnknown(subCommand)
            };
        }
        catch (HttpRequestException)
        {
            WriteError($"无法连接到 Project DNA 服务 ({_baseUrl})");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("  请先启动服务：dna serve --project <项目路径>");
            Console.ResetColor();
            return 1;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            return 1;
        }
    }

    private static List<string> ParseGlobalOptions(string[] args)
    {
        var envUrl = Environment.GetEnvironmentVariable("DNA_URL");
        if (!string.IsNullOrEmpty(envUrl))
            _baseUrl = envUrl.TrimEnd('/');

        var filtered = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                _baseUrl = args[++i].TrimEnd('/');
                continue;
            }
            filtered.Add(args[i]);
        }
        return filtered;
    }

    private static async Task<int> RunStatus()
    {
        var json = await GetJson("/api/status");
        WriteHeader("Project DNA 状态");

        var root = json.GetProperty("projectRoot").GetString();
        var configured = json.GetProperty("configured").GetBoolean();
        var modules = json.GetProperty("moduleCount").GetInt32();
        var uptime = json.GetProperty("uptime").GetString();

        Console.WriteLine($"  服务地址:   {_baseUrl}");
        Console.ForegroundColor = configured ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"  项目根目录: {(string.IsNullOrEmpty(root) ? "（未配置）" : root)}");
        Console.ResetColor();
        Console.WriteLine($"  模块数量:   {modules}");
        Console.WriteLine($"  运行时间:   {uptime}");
        return 0;
    }

    private static async Task<int> RunTopology()
    {
        var json = await GetJson("/api/topology");
        if (HasError(json)) return 1;

        WriteHeader($"模块拓扑图 — {json.GetProperty("projectRoot").GetString()}");
        Console.WriteLine($"  {json.GetProperty("summary").GetString()}");
        Console.WriteLine();

        foreach (var m in json.GetProperty("modules").EnumerateArray())
        {
            var name = m.GetProperty("name").GetString();
            var boundary = m.GetProperty("boundary").GetString();
            var maintainer = GetStringOrNull(m, "maintainer");
            var deps = m.GetProperty("dependencies");
            var depStr = deps.GetArrayLength() > 0
                ? $"  -> [{string.Join(", ", deps.EnumerateArray().Select(d => d.GetString()))}]"
                : "";
            var maintainerStr = maintainer != null ? $"  @{maintainer}" : "";
            var icon = boundary switch { "Shared" => "◈", "Soft" => "◇", _ => "◆" };

            Console.ForegroundColor = boundary switch
            {
                "Shared" => ConsoleColor.Green,
                "Soft" => ConsoleColor.Magenta,
                _ => ConsoleColor.DarkGray
            };
            Console.Write($"  {icon} ");
            Console.ResetColor();
            Console.WriteLine($"{name}{maintainerStr}{depStr}");
        }

        var edges = json.GetProperty("edges");
        if (edges.GetArrayLength() > 0)
        {
            Console.WriteLine();
            WriteSection("依赖关系");
            foreach (var e in edges.EnumerateArray())
                Console.WriteLine($"  {e.GetProperty("from").GetString()}  ->  {e.GetProperty("to").GetString()}");
        }
        return 0;
    }

    private static async Task<int> RunValidate()
    {
        var json = await GetJson("/api/governance/validate");
        if (HasError(json)) return 1;

        var healthy = json.GetProperty("healthy").GetBoolean();
        var total = json.GetProperty("totalIssues").GetInt32();

        WriteHeader("架构治理报告");

        if (healthy)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  架构健康，未发现问题。");
            Console.ResetColor();
            return 0;
        }

        Console.WriteLine($"  发现 {total} 个问题：");
        Console.WriteLine();

        foreach (var prop in new[] { "cycleSuggestions", "orphanNodes", "crossWorkIssues", "dependencyDrifts", "keyNodeWarnings" })
        {
            if (!json.TryGetProperty(prop, out var arr)) continue;
            foreach (var item in arr.EnumerateArray())
            {
                var msg = GetStringOrNull(item, "message") ?? GetStringOrNull(item, "name") ?? item.ToString();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write("  ! ");
                Console.ResetColor();
                Console.WriteLine(msg);
            }
        }
        return 0;
    }

    private static async Task<int> RunSearch(List<string> args)
    {
        var query = args.Count > 2 ? string.Join(" ", args.Skip(2)) : null;
        if (string.IsNullOrWhiteSpace(query))
        {
            WriteError("用法: dna cli search <关键词>");
            return 1;
        }

        var json = await GetJson($"/api/graph/search?q={Uri.EscapeDataString(query)}&maxResults=10");
        if (HasError(json)) return 1;

        var count = json.GetProperty("count").GetInt32();
        WriteHeader($"模块搜索 — \"{query}\" ({count} 条结果)");

        foreach (var r in json.GetProperty("results").EnumerateArray())
        {
            var name = r.GetProperty("name").GetString();
            var disc = GetStringOrNull(r, "discipline") ?? "generic";
            var summary = GetStringOrNull(r, "summary");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"  {name}");
            Console.ResetColor();
            Console.Write($" ({disc})");
            if (!string.IsNullOrEmpty(summary))
                Console.Write($"  {summary}");
            Console.WriteLine();
        }

        if (count == 0)
            Console.WriteLine($"  未找到与 \"{query}\" 相关的模块。");
        return 0;
    }

    private static async Task<int> RunRecall(List<string> args)
    {
        var question = args.Count > 2 ? string.Join(" ", args.Skip(2)) : null;
        if (string.IsNullOrWhiteSpace(question))
        {
            WriteError("用法: dna cli recall <问题>");
            return 1;
        }

        var body = new { question, maxResults = 5 };
        var content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        var response = await Http.PostAsync($"{_baseUrl}/api/memory/recall", content);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var json = doc.RootElement;
        if (HasError(json)) return 1;

        WriteHeader($"记忆检索 — \"{question}\"");

        if (json.TryGetProperty("memories", out var memories))
        {
            var i = 0;
            foreach (var m in memories.EnumerateArray())
            {
                i++;
                var summary = GetStringOrNull(m, "summary") ?? "(无摘要)";
                var score = m.TryGetProperty("score", out var s) ? s.GetDouble() : 0;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"  {i}. ");
                Console.ResetColor();
                Console.WriteLine($"{summary}  (score: {score:F3})");
            }
            if (i == 0)
                Console.WriteLine("  未找到相关记忆。");
        }
        return 0;
    }

    private static async Task<int> RunStats()
    {
        var json = await GetJson("/api/memory/stats");
        if (HasError(json)) return 1;

        WriteHeader("知识库统计");

        if (json.TryGetProperty("total", out var total))
            Console.WriteLine($"  总记忆数: {total.GetInt32()}");

        foreach (var prop in new[] { "byNodeType", "byLayer", "byType", "byDiscipline", "byFreshness" })
        {
            if (!json.TryGetProperty(prop, out var obj) || obj.ValueKind != JsonValueKind.Object) continue;
            Console.WriteLine();
            WriteSection(prop);
            foreach (var kv in obj.EnumerateObject())
                Console.WriteLine($"  {kv.Name,-30} {kv.Value.GetInt32()}");
        }
        return 0;
    }

    private static async Task<int> RunExport()
    {
        WriteHeader("导出记忆到 JSON");
        var response = await Http.PostAsync($"{_baseUrl}/api/memory/index/export", null);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var json = doc.RootElement;
        if (HasError(json)) return 1;

        var exported = json.GetProperty("exported").GetInt32();
        var skipped = json.GetProperty("skipped").GetInt32();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  导出: {exported} 条，跳过: {skipped} 条");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> RunImport()
    {
        WriteHeader("从 JSON 全量导入记忆");
        var response = await Http.PostAsync($"{_baseUrl}/api/memory/index/rebuild", null);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var json = doc.RootElement;
        if (HasError(json)) return 1;

        var imported = json.GetProperty("imported").GetInt32();
        var skipped = json.GetProperty("skipped").GetInt32();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  导入: {imported} 条，跳过: {skipped} 条");
        Console.ResetColor();
        return 0;
    }

    private static int RunHelp()
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  Project DNA CLI — 工作区引擎客户端");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("  用法: dna cli [--url http://host:port] <子命令> [参数...]");
        Console.WriteLine();

        WriteSection("查询命令");
        PrintCommandList([
            ("status", "查看服务运行状态"),
            ("topology", "扫描并显示模块拓扑图"),
            ("validate", "运行架构健康检查"),
            ("search <关键词>", "搜索模块"),
            ("recall <问题>", "语义检索记忆"),
            ("stats", "知识库统计"),
        ]);

        Console.WriteLine();
        WriteSection("运维命令");
        PrintCommandList([
            ("export", "导出记忆为 JSON 文件"),
            ("import", "从 JSON 文件全量导入记忆"),
        ]);

        Console.WriteLine();
        WriteSection("全局选项");
        Console.WriteLine("  --url <地址>    指定服务地址（默认 http://localhost:5051）");
        Console.WriteLine("                  也可设置环境变量 DNA_URL");

        Console.WriteLine();
        WriteSection("示例");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  dna cli status");
        Console.WriteLine("  dna cli validate");
        Console.WriteLine("  dna cli search combat");
        Console.WriteLine("  dna cli recall \"战斗模块有什么约束\"");
        Console.WriteLine("  dna cli --url http://192.168.1.10:5051 stats");
        Console.ResetColor();
        Console.WriteLine();
        return 0;
    }

    private static int RunUnknown(string subCommand)
    {
        WriteError($"未知子命令: '{subCommand}'");
        Console.WriteLine("  运行 'dna cli help' 查看可用命令。");
        return 1;
    }

    private static async Task<JsonElement> GetJson(string path)
    {
        var response = await Http.GetAsync($"{_baseUrl}{path}");
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement;
    }

    private static bool HasError(JsonElement json)
    {
        if (json.TryGetProperty("error", out var err))
        {
            WriteError(err.GetString()!);
            return true;
        }
        return false;
    }

    private static string? GetStringOrNull(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString();
        return null;
    }

    private static void WriteHeader(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"  ╔═ {title}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void WriteSection(string title)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.WriteLine($"  ── {title} ──");
        Console.ResetColor();
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✗ {message}");
        Console.ResetColor();
    }

    private static void PrintCommandList((string cmd, string desc)[] commands)
    {
        foreach (var (cmd, desc) in commands)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"  {cmd,-38}");
            Console.ResetColor();
            Console.WriteLine(desc);
        }
    }
}
