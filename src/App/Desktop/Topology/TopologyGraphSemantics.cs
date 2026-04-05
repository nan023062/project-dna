namespace Dna.App.Desktop.Topology;

public static class TopologyGraphSemantics
{
    public static bool IsHierarchyEdge(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        return relation is "containment" or "parentchild";
    }

    public static int ResolveHierarchyPriority(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        if (relation == "parentchild")
            return 0;

        var kind = (edge.Kind ?? string.Empty).Trim().ToLowerInvariant();
        if (kind == "composition")
            return 1;
        if (kind == "aggregation")
            return 2;
        return edge.IsComputed ? 3 : 1;
    }

    public static string ResolveRelationKey(TopologyEdgeViewModel edge)
    {
        var relation = (edge.Relation ?? string.Empty).Trim().ToLowerInvariant();
        if (relation == "dependency")
            return "dependency";
        if (relation == "collaboration")
            return "collaboration";
        if (relation == "parentchild")
            return "parentchild";
        if (relation is "composition" or "aggregation")
            return relation;
        if (relation == "containment")
        {
            var kind = (edge.Kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind is "composition" or "aggregation")
                return kind;
            return edge.IsComputed ? "aggregation" : "composition";
        }

        return "dependency";
    }

    public static bool ShouldRenderRelation(TopologyFilterState filter, string relationKey)
    {
        return relationKey switch
        {
            "dependency" => filter.ShowDependency,
            "composition" => filter.ShowComposition || filter.ShowParentChild,
            "aggregation" => filter.ShowAggregation || filter.ShowParentChild,
            "parentchild" => filter.ShowParentChild,
            "collaboration" => filter.ShowCollaboration,
            _ => true
        };
    }

    public static int ResolveLayer(TopologyNodeViewModel node)
    {
        if (node.ComputedLayer.HasValue)
            return node.ComputedLayer.Value;

        return node.Type.ToLowerInvariant() switch
        {
            "project" => 0,
            "department" => 0,
            "technical" => 1,
            "gateway" => 1,
            "team" => 2,
            _ => 1
        };
    }

    public static int ResolveTypeOrder(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "project" => 0,
            "department" => 1,
            "technical" => 2,
            "gateway" => 3,
            "team" => 4,
            _ => 9
        };
    }

    public static int CompareNodes(TopologyNodeViewModel left, TopologyNodeViewModel right)
    {
        var disciplineCompare = string.Compare(left.DisciplineLabel, right.DisciplineLabel, StringComparison.OrdinalIgnoreCase);
        if (disciplineCompare != 0)
            return disciplineCompare;

        var typeCompare = ResolveTypeOrder(left.Type).CompareTo(ResolveTypeOrder(right.Type));
        if (typeCompare != 0)
            return typeCompare;

        return string.Compare(left.Label, right.Label, StringComparison.OrdinalIgnoreCase);
    }
}
