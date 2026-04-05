using System.Globalization;
using System.Text;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Memory.Models;

namespace Dna.Knowledge.FileProtocol;

/// <summary>
/// Session 层文件协议读写，处理 .agentic-os/session 下的 Markdown + YAML frontmatter。
/// </summary>
public sealed class SessionFileStore
{
    public List<SessionFile> LoadSessions(string agenticOsPath)
    {
        var sessionRoot = FileProtocolPaths.GetSessionRoot(agenticOsPath);
        if (!Directory.Exists(sessionRoot))
            return [];

        var results = new List<SessionFile>();
        foreach (var category in new[] { FileProtocolPaths.TasksDir, FileProtocolPaths.ContextDir })
        {
            var categoryDir = Path.Combine(sessionRoot, category);
            if (!Directory.Exists(categoryDir))
                continue;

            foreach (var file in Directory.GetFiles(categoryDir, "*.md"))
            {
                var session = ParseSessionFile(file);
                if (session == null)
                    continue;

                session.Category = category;
                results.Add(session);
            }
        }

        return results;
    }

    public SessionFile? LoadSession(string filePath)
        => File.Exists(filePath) ? ParseSessionFile(filePath) : null;

    public void SaveSession(string agenticOsPath, SessionFile session)
    {
        var category = session.Category ?? InferCategory(session.Type, session.Tags);
        var categoryDir = Path.Combine(FileProtocolPaths.GetSessionRoot(agenticOsPath), category);
        Directory.CreateDirectory(categoryDir);

        var filePath = Path.Combine(categoryDir, $"{session.Id}.md");
        File.WriteAllText(filePath, SerializeSessionFile(session));
    }

    public void ExportFromEntries(string agenticOsPath, IEnumerable<MemoryEntry> entries)
    {
        foreach (var entry in entries.Where(item => item.Stage == MemoryStage.ShortTerm))
            SaveSession(agenticOsPath, FromEntry(entry));
    }

    private static SessionFile? ParseSessionFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var (frontmatter, body) = SplitFrontmatter(text);
        if (frontmatter == null)
            return null;

        var session = new SessionFile { Body = body };
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
                    session.Id = value;
                    break;
                case "type":
                    session.Type = value;
                    break;
                case "source":
                    session.Source = value;
                    break;
                case "nodeId":
                    session.NodeId = value;
                    break;
                case "createdAt":
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdAt))
                        session.CreatedAt = createdAt;
                    break;
                case "tags":
                    break;
            }

            if (key == "tags" && value.StartsWith('['))
            {
                session.Tags = value.Trim('[', ']')
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .ToList();
            }
        }

        if (session.Tags == null)
            ParseMultiLineTags(lines, session);

        return session;
    }

    private static void ParseMultiLineTags(string[] lines, SessionFile session)
    {
        string? currentKey = null;
        var currentList = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == "---")
                continue;

            if (!line.StartsWith("  ") && !line.StartsWith("\t") && !trimmed.StartsWith("- "))
            {
                if (currentKey == "tags" && currentList.Count > 0)
                    session.Tags = [.. currentList];

                currentKey = null;
                currentList.Clear();

                var colonIdx = trimmed.IndexOf(':');
                if (colonIdx < 0)
                    continue;

                var key = trimmed[..colonIdx].Trim();
                var value = trimmed[(colonIdx + 1)..].Trim();
                if (key != "tags")
                    continue;

                if (value.StartsWith('['))
                {
                    session.Tags = value.Trim('[', ']')
                        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                    continue;
                }

                if (value.Length == 0)
                    currentKey = key;
            }
            else if (trimmed.StartsWith("- ") && currentKey == "tags")
            {
                currentList.Add(trimmed[2..].Trim());
            }
        }

        if (currentKey == "tags" && currentList.Count > 0)
            session.Tags = [.. currentList];
    }

    private static (string? frontmatter, string body) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
            return (null, text);

        var secondSeparator = text.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (secondSeparator < 0)
            return (null, text);

        var frontmatter = text[3..secondSeparator].Trim();
        var body = text[(secondSeparator + 4)..].Trim();
        return (frontmatter, body);
    }

    private static string SerializeSessionFile(SessionFile session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {session.Id}");
        sb.AppendLine($"type: {session.Type}");
        sb.AppendLine($"source: {session.Source}");

        if (!string.IsNullOrWhiteSpace(session.NodeId))
            sb.AppendLine($"nodeId: {session.NodeId}");

        if (session.Tags is { Count: > 0 })
        {
            sb.AppendLine("tags:");
            foreach (var tag in session.Tags.OrderBy(item => item, StringComparer.Ordinal))
                sb.AppendLine($"  - {tag}");
        }

        sb.AppendLine($"createdAt: {session.CreatedAt:O}");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(session.Body);
        return sb.ToString();
    }

    private static SessionFile FromEntry(MemoryEntry entry)
    {
        return new SessionFile
        {
            Id = entry.Id,
            Type = entry.Type.ToString(),
            Source = entry.Source.ToString(),
            NodeId = entry.NodeId,
            Tags = entry.Tags.Count > 0 ? [.. entry.Tags] : null,
            CreatedAt = entry.CreatedAt,
            Body = entry.Content,
            Category = InferCategory(entry.Type.ToString(), entry.Tags)
        };
    }

    private static string InferCategory(string type, List<string>? tags)
    {
        if (string.Equals(type, MemoryType.Working.ToString(), StringComparison.OrdinalIgnoreCase) ||
            tags?.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase) == true)
        {
            return FileProtocolPaths.TasksDir;
        }

        return FileProtocolPaths.ContextDir;
    }
}
