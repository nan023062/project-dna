using System.Globalization;
using System.Text;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Memory.Models;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// Memory 层文件协议的读写能力。
/// 处理 .agentic-os/memory/ 下的 Markdown + YAML frontmatter 文件。
/// </summary>
public sealed class MemoryFileStore
{
    // ============== 读取 ==============

    /// <summary>扫描 memory/ 下所有 .md 文件，返回解析后的 MemoryFile 列表</summary>
    public List<MemoryFile> LoadMemories(string agenticOsPath)
    {
        var memoryRoot = FileProtocolPaths.GetMemoryRoot(agenticOsPath);
        if (!Directory.Exists(memoryRoot))
            return [];

        var results = new List<MemoryFile>();
        foreach (var category in new[] { FileProtocolPaths.DecisionsDir, FileProtocolPaths.LessonsDir,
                     FileProtocolPaths.ConventionsDir, FileProtocolPaths.SummariesDir })
        {
            var categoryDir = Path.Combine(memoryRoot, category);
            if (!Directory.Exists(categoryDir))
                continue;

            foreach (var file in Directory.GetFiles(categoryDir, "*.md"))
            {
                var memory = ParseMemoryFile(file);
                if (memory != null)
                {
                    memory.Category = category;
                    results.Add(memory);
                }
            }
        }

        return results;
    }

    /// <summary>解析单个 memory 文件</summary>
    public MemoryFile? LoadMemory(string filePath)
    {
        return File.Exists(filePath) ? ParseMemoryFile(filePath) : null;
    }

    // ============== 写入 ==============

    /// <summary>将 MemoryFile 写入到对应的分类目录</summary>
    public void SaveMemory(string agenticOsPath, MemoryFile memory)
    {
        var category = memory.Category ?? InferCategory(memory.Type);
        var categoryDir = FileProtocolPaths.GetMemoryCategoryDir(agenticOsPath, category);
        Directory.CreateDirectory(categoryDir);

        var fileName = $"{memory.Id}.md";
        var filePath = Path.Combine(categoryDir, fileName);

        var content = SerializeMemoryFile(memory);
        File.WriteAllText(filePath, content);
    }

    /// <summary>从 MemoryEntry 列表批量导出到文件</summary>
    public void ExportFromEntries(string agenticOsPath, IEnumerable<MemoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            // 只导出长期记忆
            if (entry.Stage == MemoryStage.ShortTerm)
                continue;

            var memoryFile = FromEntry(entry);
            SaveMemory(agenticOsPath, memoryFile);
        }
    }

    // ============== 解析 ==============

    private static MemoryFile? ParseMemoryFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var (frontmatter, body) = SplitFrontmatter(text);
        if (frontmatter == null)
            return null;

        var memory = new MemoryFile { Body = body };
        var lines = frontmatter.Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed == "---")
                continue;

            var colonIdx = trimmed.IndexOf(':');
            if (colonIdx < 0)
                continue;

            var key = trimmed[..colonIdx].Trim();
            var value = trimmed[(colonIdx + 1)..].Trim();

            switch (key)
            {
                case "id":
                    memory.Id = value;
                    break;
                case "type":
                    memory.Type = value;
                    break;
                case "source":
                    memory.Source = value;
                    break;
                case "nodeId":
                    memory.NodeId = value;
                    break;
                case "importance":
                    if (double.TryParse(value, CultureInfo.InvariantCulture, out var imp))
                        memory.Importance = imp;
                    break;
                case "createdAt":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var created))
                        memory.CreatedAt = created;
                    break;
                case "lastVerifiedAt":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out var verified))
                        memory.LastVerifiedAt = verified;
                    break;
                case "supersededBy":
                    memory.SupersededBy = value;
                    break;
                case "disciplines":
                    memory.Disciplines = ParseYamlList(lines, ref lines);
                    break;
                case "tags":
                    memory.Tags = ParseYamlList(lines, ref lines);
                    break;
            }

            // 处理内联列表格式 [a, b, c]
            if ((key == "disciplines" || key == "tags") && value.StartsWith('['))
            {
                var list = value.Trim('[', ']')
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
                if (key == "disciplines") memory.Disciplines = list;
                else memory.Tags = list;
            }
        }

        // 如果 disciplines/tags 是多行 YAML 列表，重新解析
        if (memory.Disciplines == null || memory.Tags == null)
        {
            var allLines = frontmatter.Split('\n');
            ParseMultiLineYamlLists(allLines, memory);
        }

        return memory;
    }

    private static void ParseMultiLineYamlLists(string[] lines, MemoryFile memory)
    {
        string? currentKey = null;
        var currentList = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == "---") continue;

            if (!line.StartsWith("  ") && !line.StartsWith("\t") && !trimmed.StartsWith("- "))
            {
                // 新的顶级 key
                if (currentKey != null)
                    AssignList(memory, currentKey, currentList);

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx >= 0)
                {
                    currentKey = trimmed[..colonIdx].Trim();
                    var value = trimmed[(colonIdx + 1)..].Trim();
                    currentList = [];

                    if (value.StartsWith('['))
                    {
                        currentList = value.Trim('[', ']')
                            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                            .ToList();
                        AssignList(memory, currentKey, currentList);
                        currentKey = null;
                    }
                    else if (value.Length > 0)
                    {
                        currentKey = null;
                    }
                }
                else
                {
                    currentKey = null;
                }
            }
            else if (trimmed.StartsWith("- ") && currentKey != null)
            {
                currentList.Add(trimmed[2..].Trim());
            }
        }

        if (currentKey != null)
            AssignList(memory, currentKey, currentList);
    }

    private static void AssignList(MemoryFile memory, string key, List<string> list)
    {
        if (list.Count == 0) return;
        switch (key)
        {
            case "disciplines":
                memory.Disciplines ??= list;
                break;
            case "tags":
                memory.Tags ??= list;
                break;
        }
    }

    private static List<string>? ParseYamlList(string[] allLines, ref string[] _)
    {
        // 占位，实际在 ParseMultiLineYamlLists 中处理
        return null;
    }

    private static (string? frontmatter, string body) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---"))
            return (null, text);

        var secondSeparator = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (secondSeparator < 0)
            return (null, text);

        var frontmatter = text[3..secondSeparator].Trim();
        var body = text[(secondSeparator + 4)..].Trim();
        return (frontmatter, body);
    }

    // ============== 序列化 ==============

    private static string SerializeMemoryFile(MemoryFile memory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {memory.Id}");
        sb.AppendLine($"type: {memory.Type}");
        sb.AppendLine($"source: {memory.Source}");

        if (memory.NodeId != null)
            sb.AppendLine($"nodeId: {memory.NodeId}");

        if (memory.Disciplines is { Count: > 0 })
        {
            sb.AppendLine("disciplines:");
            foreach (var d in memory.Disciplines.OrderBy(x => x, StringComparer.Ordinal))
                sb.AppendLine($"  - {d}");
        }

        if (memory.Tags is { Count: > 0 })
        {
            sb.AppendLine("tags:");
            foreach (var t in memory.Tags.OrderBy(x => x, StringComparer.Ordinal))
                sb.AppendLine($"  - {t}");
        }

        if (memory.Importance.HasValue)
            sb.AppendLine($"importance: {memory.Importance.Value.ToString("F1", CultureInfo.InvariantCulture)}");

        sb.AppendLine($"createdAt: {memory.CreatedAt:O}");

        if (memory.LastVerifiedAt.HasValue)
            sb.AppendLine($"lastVerifiedAt: {memory.LastVerifiedAt.Value:O}");

        if (memory.SupersededBy != null)
            sb.AppendLine($"supersededBy: {memory.SupersededBy}");

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(memory.Body);

        return sb.ToString();
    }

    // ============== 转换 ==============

    private static MemoryFile FromEntry(MemoryEntry entry)
    {
        return new MemoryFile
        {
            Id = entry.Id,
            Type = entry.Type.ToString(),
            Source = entry.Source.ToString(),
            NodeId = entry.NodeId,
            Disciplines = entry.Disciplines.Count > 0 ? entry.Disciplines : null,
            Tags = entry.Tags.Count > 0 ? entry.Tags : null,
            Importance = entry.Importance != 0.5 ? entry.Importance : null,
            CreatedAt = entry.CreatedAt,
            LastVerifiedAt = entry.LastVerifiedAt,
            SupersededBy = entry.SupersededBy,
            Body = entry.Content,
            Category = InferCategoryFromType(entry.Type)
        };
    }

    private static string InferCategory(string typeStr)
    {
        return typeStr.ToLowerInvariant() switch
        {
            "semantic" or "structural" => FileProtocolPaths.DecisionsDir,
            "episodic" => FileProtocolPaths.LessonsDir,
            "procedural" => FileProtocolPaths.ConventionsDir,
            _ => FileProtocolPaths.SummariesDir
        };
    }

    private static string InferCategoryFromType(MemoryType type)
    {
        return type switch
        {
            MemoryType.Semantic or MemoryType.Structural => FileProtocolPaths.DecisionsDir,
            MemoryType.Episodic => FileProtocolPaths.LessonsDir,
            MemoryType.Procedural => FileProtocolPaths.ConventionsDir,
            _ => FileProtocolPaths.SummariesDir
        };
    }
}
