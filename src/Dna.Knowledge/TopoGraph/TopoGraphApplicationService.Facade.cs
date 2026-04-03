namespace Dna.Knowledge;

public sealed partial class TopoGraphApplicationService
{
    private static TopologyModuleKnowledgeView ToModuleKnowledgeView(KnowledgeNode node)
    {
        return new TopologyModuleKnowledgeView
        {
            NodeId = node.Id,
            Name = node.Name,
            Type = node.Type,
            Discipline = node.Discipline,
            ParentId = node.ParentId,
            Layer = node.Layer,
            RelativePath = node.RelativePath,
            ManagedPaths = [.. node.ManagedPathScopes],
            Maintainer = node.Maintainer,
            Summary = node.Summary,
            Boundary = node.Boundary,
            PublicApi = node.PublicApi?.ToList() ?? [],
            Constraints = node.Constraints?.ToList() ?? [],
            DeclaredDependencies = [.. node.Dependencies],
            ComputedDependencies = [.. node.ComputedDependencies],
            IsCrossWorkModule = node.IsCrossWorkModule,
            Metadata = node.Metadata is { Count: > 0 }
                ? new Dictionary<string, string>(node.Metadata, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Knowledge = CloneNodeKnowledge(node.Knowledge)
        };
    }

    private static NodeKnowledge CloneNodeKnowledge(NodeKnowledge knowledge)
    {
        return new NodeKnowledge
        {
            Identity = knowledge.Identity,
            Lessons = knowledge.Lessons.Select(lesson => new LessonSummary
            {
                Title = lesson.Title,
                Severity = lesson.Severity,
                Resolution = lesson.Resolution
            }).ToList(),
            ActiveTasks = [.. knowledge.ActiveTasks],
            Facts = [.. knowledge.Facts],
            TotalMemoryCount = knowledge.TotalMemoryCount,
            IdentityMemoryId = knowledge.IdentityMemoryId,
            UpgradeTrailMemoryId = knowledge.UpgradeTrailMemoryId,
            MemoryIds = [.. knowledge.MemoryIds]
        };
    }

    private static TopologyModuleRelationView ToModuleRelationView(
        TopologyRelation relation,
        IReadOnlyDictionary<string, string> nodeNames)
    {
        return new TopologyModuleRelationView
        {
            FromId = relation.FromId,
            FromName = nodeNames.GetValueOrDefault(relation.FromId, relation.FromId),
            ToId = relation.ToId,
            ToName = nodeNames.GetValueOrDefault(relation.ToId, relation.ToId),
            Type = relation.Type,
            IsComputed = relation.IsComputed,
            Label = relation.Label
        };
    }
}
