using Dna.Knowledge;

namespace Dna.Memory.Models;

/// <summary>
/// NodeType 解析工具。
/// </summary>
public static class NodeTypeCompat
{
    public static bool TryParse(string? value, out NodeType nodeType)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            Enum.TryParse<NodeType>(value.Trim(), true, out var parsed))
        {
            nodeType = parsed;
            return true;
        }

        nodeType = default;
        return false;
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
