using System.Text;
using System.Text.Json;
using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.Memory.Store;

public partial class MemoryStore
{
    private static readonly JsonSerializerOptions TopoGraphJsonOpts = new(JsonSerializerDefaults.Web);

    internal TopoGraphIdentitySnapshot? GetIdentitySnapshot(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        var identity = Query(new MemoryFilter
        {
            Tags = [WellKnownTags.Identity],
            NodeId = nodeId,
            Freshness = FreshnessFilter.All,
            Limit = 1
        }).FirstOrDefault();

        if (identity == null)
            return null;

        try
        {
            var payload = JsonSerializer.Deserialize<IdentityPayload>(identity.Content, TopoGraphJsonOpts);
            return new TopoGraphIdentitySnapshot
            {
                Summary = identity.Summary,
                Contract = payload?.Contract,
                Description = payload?.Description,
                Keywords = payload?.Keywords ?? []
            };
        }
        catch
        {
            return new TopoGraphIdentitySnapshot
            {
                Summary = identity.Summary
            };
        }
    }

    internal TopoGraphContextContent GetContextContent(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return new TopoGraphContextContent();

        return new TopoGraphContextContent
        {
            IdentityContent = QueryMemoryContent(nodeId, WellKnownTags.Identity),
            LessonsContent = QueryMemoryContent(nodeId, WellKnownTags.Lesson),
            ActiveContent = QueryMemoryContent(nodeId, WellKnownTags.ActiveTask),
            ContractContent = GetIdentitySnapshot(nodeId)?.Contract
        };
    }

    private string? QueryMemoryContent(string nodeId, string tag)
    {
        var entries = Query(new MemoryFilter
        {
            Tags = [tag],
            NodeId = nodeId,
            Freshness = FreshnessFilter.All,
            Limit = 5
        }).OrderByDescending(e => e.Importance).ToList();

        if (entries.Count == 0)
            return null;

        return tag switch
        {
            WellKnownTags.Identity => FormatIdentityContents(entries),
            WellKnownTags.Lesson => FormatLessonContents(entries),
            WellKnownTags.ActiveTask => FormatActiveTaskContents(entries),
            _ => string.Join("\n\n", entries.Select(e => e.Content))
        };
    }

    private static string FormatIdentityContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<IdentityPayload>(entry.Content, TopoGraphJsonOpts);
                if (payload == null)
                    continue;

                sb.AppendLine($"summary: {payload.Summary}");
                if (!string.IsNullOrWhiteSpace(payload.Contract))
                    sb.AppendLine($"contract: {payload.Contract}");
                if (payload.Keywords.Count > 0)
                    sb.AppendLine($"keywords: {string.Join(", ", payload.Keywords)}");
                if (!string.IsNullOrWhiteSpace(payload.Description))
                    sb.AppendLine($"description: {payload.Description}");
                sb.AppendLine();
            }
            catch
            {
                // Ignore malformed payloads and continue formatting the rest.
            }
        }

        return sb.ToString().Trim();
    }

    private static string FormatLessonContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<LessonPayload>(entry.Content, TopoGraphJsonOpts);
                if (payload == null)
                    continue;

                sb.AppendLine($"- {payload.Title}");
                if (!string.IsNullOrWhiteSpace(payload.Severity))
                    sb.AppendLine($"  severity: {payload.Severity}");
                sb.AppendLine($"  context: {payload.Context}");
                if (!string.IsNullOrWhiteSpace(payload.Resolution))
                    sb.AppendLine($"  resolution: {payload.Resolution}");
                if (payload.Tags.Count > 0)
                    sb.AppendLine($"  tags: {string.Join(", ", payload.Tags)}");
            }
            catch
            {
                // Ignore malformed payloads and continue formatting the rest.
            }
        }

        return sb.ToString().Trim();
    }

    private static string FormatActiveTaskContents(List<MemoryEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var entry in entries)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ActiveTaskPayload>(entry.Content, TopoGraphJsonOpts);
                if (payload == null)
                    continue;

                sb.AppendLine($"task: {payload.Task}");
                if (!string.IsNullOrWhiteSpace(payload.Status))
                    sb.AppendLine($"status: {payload.Status}");
                if (!string.IsNullOrWhiteSpace(payload.Assignee))
                    sb.AppendLine($"assignee: {payload.Assignee}");
                if (payload.RelatedModules.Count > 0)
                    sb.AppendLine($"relatedModules: {string.Join(", ", payload.RelatedModules)}");
                if (!string.IsNullOrWhiteSpace(payload.Notes))
                    sb.AppendLine($"notes: {payload.Notes}");
                sb.AppendLine();
            }
            catch
            {
                // Ignore malformed payloads and continue formatting the rest.
            }
        }

        return sb.ToString().Trim();
    }
}
