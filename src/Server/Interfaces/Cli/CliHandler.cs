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
        var queries = new[]
        {
            ("status", "查看服务运行状态"),
            ("topology", "扫描并显示模块拓扑图"),
        };
        PrintCommandList(queries);

        Console.WriteLine();
        WriteSection("全局选项");
        Console.WriteLine("  --url <地址>    指定服务地址（默认 http://localhost:5051）");
        Console.WriteLine("                  也可设置环境变量 DNA_URL");

        Console.WriteLine();
        WriteSection("示例");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  dna cli status");
        Console.WriteLine("  dna cli topology");
        Console.WriteLine("  dna cli --url http://192.168.1.10:5051 status");
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
