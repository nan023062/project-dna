using System.Text.Json;
using Dna.Knowledge.TopoGraph.Models.Nodes;

namespace Dna.Knowledge;

public sealed partial class TopoGraphApplicationService
{
    public TopologyWorkbenchSnapshot GetWorkbenchSnapshot()
    {
        lock (_lock)
        {
            EnsureTopologyReadyLocked();
            return BuildWorkbenchSnapshotLocked();
        }
    }

    private TopologyWorkbenchSnapshot BuildWorkbenchSnapshotLocked()
    {
        var facadeSnapshot = _facade.GetSnapshot();
        var management = BuildManagementSnapshot(facadeSnapshot);
        var disciplineNames = BuildDisciplineDisplayNames(management, _topology.Nodes);

        var modules = _topology.Nodes
            .Select(node => ToWorkbenchModuleView(node, _topology.Nodes, disciplineNames))
            .ToList();
        var modulesByNodeId = modules
            .Where(module => !string.IsNullOrWhiteSpace(module.NodeId))
            .ToDictionary(module => module.NodeId, StringComparer.OrdinalIgnoreCase);

        var dependencyEdges = BuildWorkbenchRelationEdges(
            _topology.DependencyRelations,
            modulesByNodeId,
            TopoGraphConstants.Workbench.DependencyRelation);
        var containmentEdges = BuildWorkbenchContainmentEdges(
            _topology,
            modules,
            disciplineNames,
            modulesByNodeId);
        var collaborationEdges = BuildWorkbenchRelationEdges(
            _topology.CollaborationRelations,
            modulesByNodeId,
            TopoGraphConstants.Workbench.CollaborationRelation);
        var relationEdges = dependencyEdges
            .Concat(containmentEdges)
            .Concat(collaborationEdges)
            .ToList();
        var disciplines = BuildWorkbenchDisciplines(management, disciplineNames, modules, _topology.CrossWorks);
        var crossWorks = BuildWorkbenchCrossWorks(_topology.CrossWorks, modules);
        var teamCount = modules.Count(IsWorkbenchTeam);
        var groupCount = modules.Count - teamCount;

        return new TopologyWorkbenchSnapshot
        {
            Project = BuildWorkbenchProject(facadeSnapshot.Project),
            Modules = modules,
            Edges = dependencyEdges,
            RelationEdges = relationEdges,
            ContainmentEdges = containmentEdges,
            CollaborationEdges = collaborationEdges,
            CrossWorks = crossWorks,
            Disciplines = disciplines,
            DepMap = _topology.DepMap.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            RdepMap = _topology.RdepMap.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            Summary =
                $"Nodes {_topology.Nodes.Count}, groups {groupCount}, teams {teamCount}, dependencies {dependencyEdges.Count}, containment {containmentEdges.Count}, collaboration {collaborationEdges.Count}",
            ScannedAt = _topology.BuiltAt
        };
    }

    private static TopologyWorkbenchProjectView BuildWorkbenchProject(ProjectNode? project)
    {
        return new TopologyWorkbenchProjectView
        {
            Id = TopoGraphConstants.Workbench.ProjectNodeId,
            Name = string.IsNullOrWhiteSpace(project?.Name) ? "Project" : project.Name,
            Type = "Project",
            TypeName = "Project",
            TypeLabel = "Project",
            FileAuthority = TopoGraphConstants.Workbench.GovernAuthority,
            Summary = project?.Summary,
            ManagedPathScopes = []
        };
    }

    private static TopologyWorkbenchModuleView ToWorkbenchModuleView(
        KnowledgeNode node,
        List<KnowledgeNode> allNodes,
        Dictionary<string, string> disciplineNames)
    {
        var sameNameCount = allNodes.Count(other =>
            other.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));
        var key = sameNameCount > 1
            ? (node.RelativePath ?? node.Name).Replace('\\', '/')
            : node.Name;
        var disciplineId = string.IsNullOrWhiteSpace(node.Discipline)
            ? TopoGraphConstants.Workbench.RootDisciplineId
            : node.Discipline!;
        var typeName = ResolveWorkbenchNodeTypeName(node);
        var workflow = ParseWorkbenchMetadataList(node.Metadata, "workflow", "workflows", "process", "workDefinition", "mode");
        var rules = MergeWorkbenchUnique(node.Constraints, ParseWorkbenchMetadataList(node.Metadata, "rules", "rule", "constraints"));
        var prohibitions = ParseWorkbenchMetadataList(node.Metadata, "prohibitions", "forbidden", "forbid", "cannot");
        var publicApi = MergeWorkbenchUnique(node.PublicApi, ParseWorkbenchMetadataList(node.Metadata, "publicApi", "publicApis", "interfaces", "capabilities"));
        var managedPathScopes = BuildWorkbenchManagedPathScopes(node, typeName);
        var fileAuthority = string.Equals(typeName, "Team", StringComparison.OrdinalIgnoreCase)
            ? TopoGraphConstants.Workbench.ExecuteAuthority
            : TopoGraphConstants.Workbench.GovernAuthority;

        return new TopologyWorkbenchModuleView
        {
            Name = key,
            DisplayName = node.Name,
            NodeId = node.Id,
            RelativePath = node.RelativePath,
            Layer = node.Layer,
            Discipline = disciplineId,
            DisciplineDisplayName = disciplineNames.GetValueOrDefault(disciplineId, disciplineId),
            Type = typeName,
            TypeName = typeName,
            TypeLabel = typeName,
            Summary = node.Summary,
            Keywords = [.. node.Keywords],
            Dependencies = [.. node.Dependencies],
            ComputedDependencies = [.. node.ComputedDependencies],
            Maintainer = node.Maintainer,
            Boundary = node.Boundary,
            Contract = node.Contract,
            PublicApi = publicApi,
            Constraints = rules,
            Workflow = workflow,
            Rules = rules,
            Prohibitions = prohibitions,
            FileAuthority = fileAuthority,
            ManagedPathScopes = managedPathScopes,
            Metadata = node.Metadata is { Count: > 0 }
                ? new Dictionary<string, string>(node.Metadata, StringComparer.OrdinalIgnoreCase)
                : null,
            ParentId = node.ParentId,
            ChildIds = [.. node.ChildIds],
            ParentModuleId = node.ParentId,
            IsCrossWorkModule = node.IsCrossWorkModule,
            CanEdit = true
        };
    }

    private static List<TopologyWorkbenchRelationEdgeView> BuildWorkbenchContainmentEdges(
        TopologySnapshot topology,
        List<TopologyWorkbenchModuleView> modules,
        Dictionary<string, string> disciplineNames,
        IReadOnlyDictionary<string, TopologyWorkbenchModuleView> modulesByNodeId)
    {
        var edges = new List<TopologyWorkbenchRelationEdgeView>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        BuildProjectContainmentEdges(modules, disciplineNames, edges, seen);

        foreach (var relation in topology.ContainmentRelations)
        {
            var from = ResolveWorkbenchRelationEndpointKey(relation.FromId, modulesByNodeId);
            var to = ResolveWorkbenchRelationEndpointKey(relation.ToId, modulesByNodeId);
            TryAddWorkbenchContainmentEdge(
                from,
                to,
                string.IsNullOrWhiteSpace(relation.Label)
                    ? TopoGraphConstants.Relations.ContainmentLabel
                    : relation.Label,
                relation.IsComputed,
                edges,
                seen);
        }

        return edges;
    }

    private static List<TopologyWorkbenchRelationEdgeView> BuildWorkbenchRelationEdges(
        IEnumerable<TopologyRelation> relations,
        IReadOnlyDictionary<string, TopologyWorkbenchModuleView> modulesByNodeId,
        string relationName)
    {
        var edges = new List<TopologyWorkbenchRelationEdgeView>();

        foreach (var relation in relations)
        {
            var from = ResolveWorkbenchRelationEndpointKey(relation.FromId, modulesByNodeId);
            var to = ResolveWorkbenchRelationEndpointKey(relation.ToId, modulesByNodeId);
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            edges.Add(new TopologyWorkbenchRelationEdgeView
            {
                From = from,
                To = to,
                Relation = relationName,
                Kind = relation.Label,
                IsComputed = relation.IsComputed
            });
        }

        return edges;
    }

    private static void BuildProjectContainmentEdges(
        List<TopologyWorkbenchModuleView> modules,
        Dictionary<string, string> disciplineNames,
        List<TopologyWorkbenchRelationEdgeView> edges,
        HashSet<string> seen)
    {
        var departmentIds = modules
            .Select(module => module.Discipline)
            .Where(id =>
                !string.IsNullOrWhiteSpace(id) &&
                !string.Equals(id, TopoGraphConstants.Workbench.RootDisciplineId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var disciplineId in disciplineNames.Keys
                     .Where(id =>
                         !string.IsNullOrWhiteSpace(id) &&
                         !string.Equals(id, TopoGraphConstants.Workbench.RootDisciplineId, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!departmentIds.Contains(disciplineId, StringComparer.OrdinalIgnoreCase))
                departmentIds.Add(disciplineId);
        }

        foreach (var disciplineId in departmentIds)
        {
            TryAddWorkbenchContainmentEdge(
                TopoGraphConstants.Workbench.ProjectNodeId,
                BuildWorkbenchDepartmentNodeId(disciplineId),
                TopoGraphConstants.Relations.ContainmentLabel,
                false,
                edges,
                seen);
        }

        foreach (var module in modules.Where(module => string.IsNullOrWhiteSpace(module.ParentId)))
        {
            var parentNode = string.Equals(module.Discipline, TopoGraphConstants.Workbench.RootDisciplineId, StringComparison.OrdinalIgnoreCase)
                ? TopoGraphConstants.Workbench.ProjectNodeId
                : BuildWorkbenchDepartmentNodeId(module.Discipline);

            TryAddWorkbenchContainmentEdge(
                parentNode,
                module.Name,
                TopoGraphConstants.Relations.ContainmentLabel,
                false,
                edges,
                seen);
        }
    }

    private static void TryAddWorkbenchContainmentEdge(
        string from,
        string to,
        string kind,
        bool isComputed,
        List<TopologyWorkbenchRelationEdgeView> edges,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            return;

        var key = $"{from}|{to}";
        if (!seen.Add(key))
            return;

        edges.Add(new TopologyWorkbenchRelationEdgeView
        {
            From = from,
            To = to,
            Relation = TopoGraphConstants.Workbench.ContainmentRelation,
            Kind = kind,
            IsComputed = isComputed
        });
    }

    private static List<TopologyWorkbenchCrossWorkView> BuildWorkbenchCrossWorks(
        List<CrossWork> crossWorks,
        List<TopologyWorkbenchModuleView> modules)
    {
        var moduleByName = modules.ToDictionary(module => module.DisplayName, module => module.Name, StringComparer.OrdinalIgnoreCase);
        return crossWorks.Select(crossWork => new TopologyWorkbenchCrossWorkView
        {
            Id = crossWork.Id,
            Name = crossWork.Name,
            Description = crossWork.Description,
            Feature = crossWork.Feature,
            Participants = crossWork.Participants.Select(participant => new TopologyWorkbenchCrossWorkParticipantView
            {
                ModuleName = participant.ModuleName,
                ModuleId = moduleByName.GetValueOrDefault(participant.ModuleName),
                Role = participant.Role,
                Contract = participant.Contract,
                ContractType = participant.ContractType,
                Deliverable = participant.Deliverable
            }).ToList()
        }).ToList();
    }

    private static List<TopologyWorkbenchDisciplineView> BuildWorkbenchDisciplines(
        TopologyManagementSnapshot management,
        Dictionary<string, string> disciplineNames,
        List<TopologyWorkbenchModuleView> modules,
        List<CrossWork> crossWorks)
    {
        var technicalModules = modules.Where(module => !IsWorkbenchTeam(module)).ToList();
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var disciplineId in disciplineNames.Keys
                     .Where(id => !string.Equals(id, TopoGraphConstants.Workbench.RootDisciplineId, StringComparison.OrdinalIgnoreCase)))
        {
            knownIds.Add(disciplineId);
        }

        foreach (var module in technicalModules.Where(module => !string.Equals(module.Discipline, TopoGraphConstants.Workbench.RootDisciplineId, StringComparison.OrdinalIgnoreCase)))
            knownIds.Add(module.Discipline);

        var list = new List<TopologyWorkbenchDisciplineView>();
        foreach (var id in knownIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            var definition = management.Disciplines.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            var moduleNames = technicalModules
                .Where(module => string.Equals(module.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(module => module.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayNameSet = technicalModules
                .Where(module => string.Equals(module.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(module => module.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var crossWorkCount = crossWorks.Count(crossWork =>
                crossWork.Participants.Any(participant => displayNameSet.Contains(participant.ModuleName)));

            list.Add(new TopologyWorkbenchDisciplineView
            {
                Id = id,
                DisplayName = disciplineNames.GetValueOrDefault(id, id),
                RoleId = definition?.RoleId,
                ModuleCount = moduleNames.Count,
                Modules = moduleNames,
                CrossWorkCount = crossWorkCount
            });
        }

        return list;
    }

    private static Dictionary<string, string> BuildDisciplineDisplayNames(
        TopologyManagementSnapshot management,
        List<KnowledgeNode> nodes)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var discipline in management.Disciplines)
        {
            if (string.IsNullOrWhiteSpace(discipline.Id))
                continue;

            map[discipline.Id] = string.IsNullOrWhiteSpace(discipline.DisplayName)
                ? discipline.Id
                : discipline.DisplayName.Trim();
        }

        foreach (var node in nodes)
        {
            var id = string.IsNullOrWhiteSpace(node.Discipline)
                ? TopoGraphConstants.Workbench.RootDisciplineId
                : node.Discipline!;
            if (!map.ContainsKey(id))
                map[id] = id;
        }

        if (!map.ContainsKey(TopoGraphConstants.Workbench.RootDisciplineId))
            map[TopoGraphConstants.Workbench.RootDisciplineId] = TopoGraphConstants.Workbench.RootDisciplineId;

        return map;
    }

    private static bool IsWorkbenchTeam(TopologyWorkbenchModuleView module)
        => string.Equals(module.TypeName, "Team", StringComparison.OrdinalIgnoreCase) || module.IsCrossWorkModule;

    private static string ResolveWorkbenchNodeTypeName(KnowledgeNode node)
    {
        if (node.IsCrossWorkModule)
            return "Team";

        return node.Type switch
        {
            NodeType.Project => "Project",
            NodeType.Department => "Department",
            NodeType.Team => "Team",
            _ => "Technical"
        };
    }

    private static List<string> BuildWorkbenchManagedPathScopes(KnowledgeNode node, string typeName)
    {
        if (node.ManagedPathScopes.Count > 0)
            return MergeWorkbenchUnique(node.ManagedPathScopes);

        var metadataScopes = ParseWorkbenchMetadataList(node.Metadata, "managedPathScopes", "pathScopes", "managedPaths");
        var normalizedPath = NormalizeWorkbenchPath(node.RelativePath);
        var baseScopes = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            baseScopes.Add(normalizedPath);
        if (string.Equals(typeName, "Team", StringComparison.OrdinalIgnoreCase))
            return metadataScopes;

        return MergeWorkbenchUnique(baseScopes, metadataScopes);
    }

    private static List<string> ParseWorkbenchMetadataList(Dictionary<string, string>? metadata, params string[] keys)
    {
        if (metadata == null || metadata.Count == 0 || keys.Length == 0)
            return [];

        var values = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            values.AddRange(ParseWorkbenchLooseList(raw));
        }

        return MergeWorkbenchUnique(values);
    }

    private static List<string> ParseWorkbenchLooseList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var text = raw.Trim();
        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var array = JsonSerializer.Deserialize<List<string>>(text);
                if (array is { Count: > 0 })
                    return MergeWorkbenchUnique(array);
            }
            catch
            {
                // Fall through to delimiter parsing.
            }
        }

        var parts = text.Split(
            ['\n', ';', '|', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length <= 1
            ? MergeWorkbenchUnique([CleanWorkbenchListItem(text)])
            : MergeWorkbenchUnique(parts.Select(CleanWorkbenchListItem));
    }

    private static string CleanWorkbenchListItem(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        var value = input.Trim();
        while (value.StartsWith("- ", StringComparison.Ordinal) || value.StartsWith("* ", StringComparison.Ordinal))
            value = value[2..].TrimStart();

        return value.Trim();
    }

    private static List<string> MergeWorkbenchUnique(params IEnumerable<string>?[] sources)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (source == null)
                continue;

            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(item))
                    continue;

                var normalized = item.Trim();
                if (normalized.Length == 0 || !seen.Add(normalized))
                    continue;

                result.Add(normalized);
            }
        }

        return result;
    }

    private static string ResolveWorkbenchRelationEndpointKey(
        string relationNodeId,
        IReadOnlyDictionary<string, TopologyWorkbenchModuleView> modulesByNodeId)
    {
        if (string.IsNullOrWhiteSpace(relationNodeId))
            return string.Empty;

        return modulesByNodeId.TryGetValue(relationNodeId, out var module)
            ? module.Name
            : relationNodeId;
    }

    private static string BuildWorkbenchDepartmentNodeId(string disciplineId)
        => $"{TopoGraphConstants.Workbench.DepartmentNodePrefix}{disciplineId}";

    private static string? NormalizeWorkbenchPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }
}
