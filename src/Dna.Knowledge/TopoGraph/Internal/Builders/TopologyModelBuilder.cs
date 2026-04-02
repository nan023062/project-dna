using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using Dna.Knowledge.TopoGraph.Models.Relations;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using Dna.Knowledge.TopoGraph.Models.Validation;
using TopologyRelationKindModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelationKind;
using TopologyRelationModel = Dna.Knowledge.TopoGraph.Models.Relations.TopologyRelation;

namespace Dna.Knowledge.TopoGraph.Internal.Builders;

public sealed class TopologyModelBuilder
{
    public TopologyModelBuildResult Build(TopologyModelDefinition definition)
    {
        var issues = new List<TopologyValidationIssue>();
        var nodes = new List<TopologyNode>();
        var nodeMap = new Dictionary<string, TopologyNode>(StringComparer.OrdinalIgnoreCase);

        var project = BuildProject(definition.Project, issues);
        if (project != null)
            AddNode(nodes, nodeMap, project, issues);

        foreach (var registration in definition.Departments)
            AddNode(nodes, nodeMap, BuildDepartment(registration), issues);

        foreach (var registration in definition.TechnicalNodes)
            AddNode(nodes, nodeMap, BuildTechnical(registration), issues);

        foreach (var registration in definition.TeamNodes)
            AddNode(nodes, nodeMap, BuildTeam(registration), issues);

        BuildHierarchy(nodeMap, issues);

        var relations = new List<TopologyRelationModel>();
        relations.AddRange(BuildContainmentRelations(nodeMap.Values));
        relations.AddRange(BuildDependencyRelations(nodeMap, issues));
        relations.AddRange(BuildCollaborationRelations(nodeMap, definition, issues));

        return new TopologyModelBuildResult
        {
            Snapshot = new TopologyModelSnapshot
            {
                Project = project,
                Nodes = [.. nodes],
                Relations = relations,
                NodeMap = new Dictionary<string, TopologyNode>(nodeMap, StringComparer.OrdinalIgnoreCase)
            },
            Issues = issues
        };
    }

    private static ProjectNode? BuildProject(ProjectNodeRegistration? registration, List<TopologyValidationIssue> issues)
    {
        if (registration == null)
        {
            issues.Add(Error("project.missing", "TopoGraph 新模型必须存在唯一的 ProjectNode。"));
            return null;
        }

        if (!string.IsNullOrWhiteSpace(registration.ParentId))
        {
            issues.Add(Error(
                "project.parent.forbidden",
                $"ProjectNode '{registration.Id}' 不能声明 ParentId。",
                registration.Id));
        }

        return new ProjectNode
        {
            Id = registration.Id,
            Name = registration.Name,
            Summary = registration.Summary,
            ParentId = null,
            Vision = registration.Vision,
            WorkspaceRoot = registration.WorkspaceRoot,
            Steward = registration.Steward,
            Knowledge = registration.Knowledge,
            Metadata = registration.Metadata
        };
    }

    private static DepartmentNode BuildDepartment(DepartmentNodeRegistration registration)
    {
        return new DepartmentNode
        {
            Id = registration.Id,
            Name = registration.Name,
            Summary = registration.Summary,
            ParentId = registration.ParentId,
            DisciplineCode = registration.DisciplineCode,
            Scope = registration.Scope,
            Owner = registration.Owner,
            Knowledge = registration.Knowledge,
            Metadata = registration.Metadata
        };
    }

    private static TechnicalNode BuildTechnical(TechnicalNodeRegistration registration)
    {
        return new TechnicalNode
        {
            Id = registration.Id,
            Name = registration.Name,
            Summary = registration.Summary,
            ParentId = registration.ParentId,
            PathBinding = registration.PathBinding,
            Maintainer = registration.Maintainer,
            Contract = registration.Contract,
            DeclaredDependencies = registration.DeclaredDependencies.ToList(),
            ComputedDependencies = registration.ComputedDependencies.ToList(),
            CapabilityTags = registration.CapabilityTags.ToList(),
            Knowledge = registration.Knowledge,
            Metadata = registration.Metadata
        };
    }

    private static TeamNode BuildTeam(TeamNodeRegistration registration)
    {
        return new TeamNode
        {
            Id = registration.Id,
            Name = registration.Name,
            Summary = registration.Summary,
            ParentId = registration.ParentId,
            PathBinding = registration.PathBinding,
            Maintainer = registration.Maintainer,
            BusinessObjective = registration.BusinessObjective,
            TechnicalDependencies = registration.TechnicalDependencies.ToList(),
            Deliverables = registration.Deliverables.ToList(),
            CollaborationIds = registration.CollaborationIds.ToList(),
            Knowledge = registration.Knowledge,
            Metadata = registration.Metadata
        };
    }

    private static void AddNode(
        List<TopologyNode> nodes,
        Dictionary<string, TopologyNode> nodeMap,
        TopologyNode node,
        List<TopologyValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(node.Id))
        {
            issues.Add(Error("node.id.empty", $"发现空节点 Id，节点名称为 '{node.Name}'。"));
            return;
        }

        if (string.IsNullOrWhiteSpace(node.Name))
        {
            issues.Add(Error("node.name.empty", $"节点 '{node.Id}' 缺少 Name。", node.Id));
            return;
        }

        if (!nodeMap.TryAdd(node.Id, node))
        {
            issues.Add(Error("node.duplicate", $"节点 Id '{node.Id}' 重复。", node.Id));
            return;
        }

        nodes.Add(node);
    }

    private static void BuildHierarchy(
        Dictionary<string, TopologyNode> nodeMap,
        List<TopologyValidationIssue> issues)
    {
        foreach (var node in nodeMap.Values)
            node.ChildIds.Clear();

        foreach (var node in nodeMap.Values)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId))
                continue;

            if (!nodeMap.TryGetValue(node.ParentId, out var parent))
            {
                issues.Add(Error(
                    "node.parent.missing",
                    $"节点 '{node.Id}' 声明的父节点 '{node.ParentId}' 不存在。",
                    node.Id));
                continue;
            }

            if (!IsAllowedParent(parent.Kind, node.Kind))
            {
                issues.Add(Error(
                    "node.parent.invalid",
                    $"节点 '{node.Id}' 的父子结构非法: {parent.Kind} -> {node.Kind}。",
                    node.Id));
                continue;
            }

            parent.ChildIds.Add(node.Id);
        }
    }

    private static List<TopologyRelationModel> BuildContainmentRelations(IEnumerable<TopologyNode> nodes)
    {
        var relations = new List<TopologyRelationModel>();

        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.ParentId))
                continue;

            relations.Add(new TopologyRelationModel
            {
                FromId = node.ParentId,
                ToId = node.Id,
                Kind = TopologyRelationKindModel.Containment,
                IsComputed = false,
                Label = "containment"
            });
        }

        return relations;
    }

    private static List<TopologyRelationModel> BuildDependencyRelations(
        Dictionary<string, TopologyNode> nodeMap,
        List<TopologyValidationIssue> issues)
    {
        var relations = new List<TopologyRelationModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var technical in nodeMap.Values.OfType<TechnicalNode>())
        {
            foreach (var targetId in technical.DeclaredDependencies)
            {
                AddDependencyRelation(
                    relations,
                    seen,
                    nodeMap,
                    issues,
                    technical.Id,
                    targetId,
                    isComputed: false);
            }

            foreach (var targetId in technical.ComputedDependencies)
            {
                AddDependencyRelation(
                    relations,
                    seen,
                    nodeMap,
                    issues,
                    technical.Id,
                    targetId,
                    isComputed: true);
            }
        }

        foreach (var team in nodeMap.Values.OfType<TeamNode>())
        {
            foreach (var targetId in team.TechnicalDependencies)
            {
                AddDependencyRelation(
                    relations,
                    seen,
                    nodeMap,
                    issues,
                    team.Id,
                    targetId,
                    isComputed: false);
            }
        }

        return relations;
    }

    private static void AddDependencyRelation(
        List<TopologyRelationModel> relations,
        HashSet<string> seen,
        Dictionary<string, TopologyNode> nodeMap,
        List<TopologyValidationIssue> issues,
        string fromId,
        string toId,
        bool isComputed)
    {
        if (!nodeMap.TryGetValue(fromId, out var source))
        {
            issues.Add(Error("dependency.source.missing", $"依赖源节点 '{fromId}' 不存在。", fromId));
            return;
        }

        if (!nodeMap.TryGetValue(toId, out var target))
        {
            issues.Add(Error("dependency.target.missing", $"依赖目标节点 '{toId}' 不存在。", fromId));
            return;
        }

        if (source.Kind is TopologyNodeKind.Project or TopologyNodeKind.Department)
        {
            issues.Add(Error(
                "dependency.source.invalid",
                $"分组节点 '{source.Id}' 不能发出依赖关系。",
                source.Id));
            return;
        }

        if (target.Kind != TopologyNodeKind.Technical)
        {
            issues.Add(Error(
                "dependency.target.invalid",
                $"节点 '{source.Id}' 只能依赖 TechnicalNode，当前目标为 {target.Kind}。",
                source.Id));
            return;
        }

        var key = $"{fromId}|{toId}";
        if (!seen.Add(key))
            return;

        relations.Add(new TopologyRelationModel
        {
            FromId = fromId,
            ToId = toId,
            Kind = TopologyRelationKindModel.Dependency,
            IsComputed = isComputed,
            Label = isComputed ? "computed" : "declared"
        });
    }

    private static List<TopologyRelationModel> BuildCollaborationRelations(
        Dictionary<string, TopologyNode> nodeMap,
        TopologyModelDefinition definition,
        List<TopologyValidationIssue> issues)
    {
        var relations = new List<TopologyRelationModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var team in nodeMap.Values.OfType<TeamNode>())
        {
            foreach (var targetId in team.CollaborationIds)
            {
                AddCollaborationRelation(
                    relations,
                    seen,
                    nodeMap,
                    issues,
                    team.Id,
                    targetId,
                    team.Name);
            }
        }

        foreach (var collaboration in definition.Collaborations)
        {
            AddCollaborationRelation(
                relations,
                seen,
                nodeMap,
                issues,
                collaboration.FromId,
                collaboration.ToId,
                collaboration.Label);
        }

        return relations;
    }

    private static void AddCollaborationRelation(
        List<TopologyRelationModel> relations,
        HashSet<string> seen,
        Dictionary<string, TopologyNode> nodeMap,
        List<TopologyValidationIssue> issues,
        string fromId,
        string toId,
        string? label)
    {
        if (!nodeMap.TryGetValue(fromId, out var source))
        {
            issues.Add(Error("collaboration.source.missing", $"协作源节点 '{fromId}' 不存在。", fromId));
            return;
        }

        if (!nodeMap.TryGetValue(toId, out var target))
        {
            issues.Add(Error("collaboration.target.missing", $"协作目标节点 '{toId}' 不存在。", fromId));
            return;
        }

        if (source.Kind == TopologyNodeKind.Project || target.Kind == TopologyNodeKind.Project)
        {
            issues.Add(Error(
                "collaboration.project.forbidden",
                $"ProjectNode 不应直接参与协作关系: {fromId} -> {toId}。",
                fromId));
            return;
        }

        if (source.Kind != TopologyNodeKind.Team && target.Kind != TopologyNodeKind.Team)
        {
            issues.Add(Error(
                "collaboration.team.required",
                $"协作关系至少一端必须是 TeamNode: {fromId} -> {toId}。",
                fromId));
            return;
        }

        var ordered = string.Compare(fromId, toId, StringComparison.OrdinalIgnoreCase) <= 0
            ? (fromId, toId)
            : (toId, fromId);
        var key = $"{ordered.Item1}|{ordered.Item2}";
        if (!seen.Add(key))
            return;

        relations.Add(new TopologyRelationModel
        {
            FromId = ordered.Item1,
            ToId = ordered.Item2,
            Kind = TopologyRelationKindModel.Collaboration,
            IsComputed = false,
            Label = label
        });
    }

    private static bool IsAllowedParent(TopologyNodeKind parentKind, TopologyNodeKind childKind)
    {
        return childKind switch
        {
            TopologyNodeKind.Project => false,
            TopologyNodeKind.Department => parentKind is TopologyNodeKind.Project or TopologyNodeKind.Department,
            TopologyNodeKind.Technical => parentKind == TopologyNodeKind.Department,
            TopologyNodeKind.Team => parentKind == TopologyNodeKind.Department,
            _ => false
        };
    }

    private static TopologyValidationIssue Error(string code, string message, string? nodeId = null)
    {
        return new TopologyValidationIssue
        {
            Severity = TopologyValidationSeverity.Error,
            Code = code,
            Message = message,
            NodeId = nodeId
        };
    }
}
