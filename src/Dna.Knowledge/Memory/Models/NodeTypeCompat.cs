using Dna.Knowledge;

namespace Dna.Memory.Models;

/// <summary>
/// NodeType 兼容工具：
/// - 解析新语义（Project/Department/Technical/Team）
/// - 兼容旧层级命名（ProjectVision/DisciplineStandard/...）
/// </summary>
public static class NodeTypeCompat
{
    private static readonly Dictionary<string, NodeType> LegacyNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ProjectVision"] = NodeType.Project,
        ["DisciplineStandard"] = NodeType.Department,
        ["CrossDiscipline"] = NodeType.Team,
        ["FeatureSystem"] = NodeType.Technical,
        ["Implementation"] = NodeType.Team,
        ["Root"] = NodeType.Project,
        ["Module"] = NodeType.Technical,
        ["Group"] = NodeType.Technical,
        ["CrossWork"] = NodeType.Team
    };

    public static bool TryParse(string? value, out NodeType nodeType)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (Enum.TryParse<NodeType>(value, true, out var parsed))
            {
                nodeType = parsed;
                return true;
            }

            if (LegacyNameMap.TryGetValue(value.Trim(), out var legacyMapped))
            {
                nodeType = legacyMapped;
                return true;
            }
        }

        nodeType = default;
        return false;
    }

    public static NodeType Resolve(NodeType? nodeType, string? legacyLayer = null, NodeType fallback = NodeType.Technical)
    {
        if (nodeType.HasValue) return nodeType.Value;
        return TryParse(legacyLayer, out var parsed) ? parsed : fallback;
    }

    public static List<NodeType>? ResolveList(List<NodeType>? nodeTypes, List<string>? legacyLayers)
    {
        if (nodeTypes is { Count: > 0 })
            return nodeTypes.Distinct().ToList();

        if (legacyLayers is not { Count: > 0 })
            return null;

        var parsed = legacyLayers
            .Select(layer => TryParse(layer, out var nodeType) ? (NodeType?)nodeType : null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .Distinct()
            .ToList();

        return parsed.Count > 0 ? parsed : null;
    }

    public static int GovernanceOrder(NodeType nodeType) => nodeType switch
    {
        NodeType.Project => 0,
        NodeType.Department => 1,
        NodeType.Technical => 2,
        NodeType.Team => 3,
        _ => 99
    };
}
