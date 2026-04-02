namespace Dna.App.Desktop.Topology;

public sealed class TopologyScene
{
    private readonly Dictionary<string, TopologyNodeViewModel> _nodeMap;
    private readonly Dictionary<string, string> _parentByChild;
    private readonly Dictionary<string, List<string>> _childrenByParent;
    private readonly List<string> _rootNodeIds;

    private TopologyScene(
        IReadOnlyList<TopologyNodeViewModel> nodes,
        IReadOnlyList<TopologyEdgeViewModel> edges,
        Dictionary<string, TopologyNodeViewModel> nodeMap,
        Dictionary<string, string> parentByChild,
        Dictionary<string, List<string>> childrenByParent,
        List<string> rootNodeIds)
    {
        Nodes = nodes;
        Edges = edges;
        _nodeMap = nodeMap;
        _parentByChild = parentByChild;
        _childrenByParent = childrenByParent;
        _rootNodeIds = rootNodeIds;
        HasLayerData = nodes.Any(n => n.ComputedLayer.HasValue);
    }

    public static TopologyScene Empty { get; } = Create([], []);

    public IReadOnlyList<TopologyNodeViewModel> Nodes { get; }
    public IReadOnlyList<TopologyEdgeViewModel> Edges { get; }
    public bool HasLayerData { get; }
    public IReadOnlyDictionary<string, TopologyNodeViewModel> NodeMap => _nodeMap;
    public IReadOnlyDictionary<string, string> ParentByChild => _parentByChild;
    public IReadOnlyDictionary<string, List<string>> ChildrenByParent => _childrenByParent;
    public IReadOnlyList<string> RootNodeIds => _rootNodeIds;

    public static TopologyScene Create(IReadOnlyList<TopologyNodeViewModel> nodes, IReadOnlyList<TopologyEdgeViewModel> edges)
    {
        var nodeMap = new Dictionary<string, TopologyNodeViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            nodeMap[node.Id] = node;

        var parentByChild = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var childrenByParent = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var rootNodeIds = new List<string>();

        var hierarchyEdges = edges
            .Where(TopologyGraphSemantics.IsHierarchyEdge)
            .OrderBy(TopologyGraphSemantics.ResolveHierarchyPriority)
            .ThenBy(edge => edge.From, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.To, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var edge in hierarchyEdges)
        {
            if (!nodeMap.ContainsKey(edge.From) || !nodeMap.ContainsKey(edge.To))
                continue;

            if (!parentByChild.TryAdd(edge.To, edge.From))
                continue;

            if (!childrenByParent.TryGetValue(edge.From, out var children))
            {
                children = [];
                childrenByParent[edge.From] = children;
            }

            children.Add(edge.To);
        }

        foreach (var node in nodes)
        {
            if (!parentByChild.ContainsKey(node.Id))
                rootNodeIds.Add(node.Id);
        }

        if (rootNodeIds.Count == 0)
            rootNodeIds.AddRange(nodes.Select(node => node.Id));

        foreach (var pair in childrenByParent)
            pair.Value.Sort((leftId, rightId) => TopologyGraphSemantics.CompareNodes(nodeMap[leftId], nodeMap[rightId]));

        rootNodeIds.Sort((leftId, rightId) => TopologyGraphSemantics.CompareNodes(nodeMap[leftId], nodeMap[rightId]));

        return new TopologyScene(nodes, edges, nodeMap, parentByChild, childrenByParent, rootNodeIds);
    }

    public bool ContainsNode(string? nodeId)
        => !string.IsNullOrWhiteSpace(nodeId) && _nodeMap.ContainsKey(nodeId);

    public TopologyNodeViewModel? GetNodeOrDefault(string? nodeId)
        => string.IsNullOrWhiteSpace(nodeId) ? null : _nodeMap.GetValueOrDefault(nodeId);

    public bool CanNavigateInto(string? nodeId)
        => ResolveScopedChildIds(nodeId).Count > 0;

    public int GetChildCount(string? nodeId)
        => ResolveScopedChildIds(nodeId).Count;

    public string? ResolveParent(string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        return _parentByChild.TryGetValue(nodeId, out var parentId) ? parentId : null;
    }

    public IReadOnlyList<string> BuildScopeTrailIds(string? viewRootId)
    {
        if (string.IsNullOrWhiteSpace(viewRootId))
            return Array.Empty<string>();

        var trail = new List<string>();
        var current = viewRootId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard < 64)
        {
            trail.Insert(0, current);
            current = ResolveParent(current);
            guard++;
        }

        return trail;
    }

    public List<string> ResolveScopedChildIds(string? parentId)
    {
        if (string.IsNullOrWhiteSpace(parentId) ||
            !_childrenByParent.TryGetValue(parentId, out var children) ||
            children.Count == 0)
        {
            return [];
        }

        var scoped = children
            .Where(childId => _nodeMap.ContainsKey(childId))
            .ToList();

        if (!_nodeMap.TryGetValue(parentId, out var parent))
            return scoped;

        if (!string.Equals(parent.Type, "Project", StringComparison.OrdinalIgnoreCase))
            return scoped;

        var departments = scoped
            .Where(childId =>
                _nodeMap.TryGetValue(childId, out var child) &&
                string.Equals(child.Type, "Department", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return departments.Count > 0 ? departments : scoped;
    }

    public TopologyVisibleGraph ResolveVisibleGraph(TopologyFilterState filter, string? viewRootId)
    {
        var visibleIds = ResolveVisibleNodeIds(viewRootId);
        var visibleNodes = new List<TopologyNodeViewModel>(visibleIds.Count);
        foreach (var id in visibleIds)
        {
            if (_nodeMap.TryGetValue(id, out var node))
                visibleNodes.Add(node);
        }

        var visibleSet = new HashSet<string>(visibleNodes.Select(node => node.Id), StringComparer.OrdinalIgnoreCase);
        var mergedEdges = new Dictionary<string, TopologyEdgeViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in Edges)
        {
            var relationKey = TopologyGraphSemantics.ResolveRelationKey(edge);
            if (!TopologyGraphSemantics.ShouldRenderRelation(filter, relationKey))
                continue;

            var from = CollapseToVisibleNode(edge.From, visibleSet, viewRootId);
            var to = CollapseToVisibleNode(edge.To, visibleSet, viewRootId);

            if (string.IsNullOrWhiteSpace(from) ||
                string.IsNullOrWhiteSpace(to) ||
                string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TopologyGraphSemantics.IsHierarchyEdge(edge) &&
                (!visibleSet.Contains(from) || !visibleSet.Contains(to)))
            {
                continue;
            }

            var key = $"{relationKey}|{edge.Kind}|{from}|{to}";
            if (mergedEdges.TryGetValue(key, out var existing))
            {
                if (existing.IsComputed && !edge.IsComputed)
                    mergedEdges[key] = existing with { IsComputed = false };
                continue;
            }

            mergedEdges[key] = edge with
            {
                From = from,
                To = to
            };
        }

        return new TopologyVisibleGraph(visibleNodes, mergedEdges.Values.ToList(), visibleSet);
    }

    private List<string> ResolveVisibleNodeIds(string? viewRootId)
    {
        if (Nodes.Count == 0)
            return [];

        if (string.IsNullOrWhiteSpace(viewRootId) || !_nodeMap.ContainsKey(viewRootId))
            return [.. _rootNodeIds];

        var children = ResolveScopedChildIds(viewRootId);
        if (children.Count == 0)
            return [viewRootId];

        var ids = new List<string>(children.Count + 1) { viewRootId };
        ids.AddRange(children.Where(childId => !string.Equals(childId, viewRootId, StringComparison.OrdinalIgnoreCase)));
        return ids;
    }

    private string? CollapseToVisibleNode(string nodeId, HashSet<string> visibleSet, string? viewRootId)
    {
        if (visibleSet.Contains(nodeId))
            return nodeId;

        var current = nodeId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard < 64)
        {
            if (_parentByChild.TryGetValue(current, out var parentId))
            {
                current = parentId;
                if (visibleSet.Contains(current))
                    return current;
                guard++;
                continue;
            }

            break;
        }

        if (!string.IsNullOrWhiteSpace(viewRootId) && visibleSet.Contains(viewRootId))
            return viewRootId;

        return null;
    }
}
