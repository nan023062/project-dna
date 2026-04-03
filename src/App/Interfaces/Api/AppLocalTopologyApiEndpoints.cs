using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.App.Services;
using Dna.Knowledge;
using Dna.Knowledge.Workspace.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalTopologyApiEndpoints
{
    private static readonly string[] FixedDepartmentIds = ["product-design", "engineering", "devops"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapAppLocalTopologyApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/topology", ([FromServices] ITopoGraphApplicationService topology, [FromServices] AppRuntimeOptions runtime) =>
        {
            var topo = topology.BuildTopology();
            var management = topology.GetManagementSnapshot();
            var disciplineNames = BuildDisciplineDisplayNames(management, topo.Nodes);

            var moduleDtos = topo.Nodes
                .Select(n => ToModuleDto(n, topo.Nodes, disciplineNames))
                .ToList();
            var modulesByNodeId = moduleDtos
                .Where(m => !string.IsNullOrWhiteSpace(m.NodeId))
                .ToDictionary(m => m.NodeId, StringComparer.OrdinalIgnoreCase);

            var dependencyEdges = BuildRelationEdges(
                topo.DependencyRelations,
                modulesByNodeId,
                "dependency");

            var containmentEdges = BuildContainmentEdges(topo, moduleDtos, disciplineNames, modulesByNodeId);
            var collaborationEdges = BuildRelationEdges(
                topo.CollaborationRelations,
                modulesByNodeId,
                "collaboration");
            var relationEdges = dependencyEdges.Concat(containmentEdges).Concat(collaborationEdges).ToList();
            var disciplineDtos = BuildDisciplineDtos(management, disciplineNames, moduleDtos, topo.CrossWorks);
            var crossWorkDtos = BuildCrossWorkDtos(topo.CrossWorks, moduleDtos);
            var teamCount = moduleDtos.Count(IsTeam);
            var groupCount = moduleDtos.Count - teamCount;

            return Results.Json(new
            {
                project = new
                {
                    id = "project",
                    name = runtime.ProjectName,
                    type = "Project",
                    typeName = "Project",
                    typeLabel = "Project",
                    fileAuthority = "govern",
                    managedPathScopes = Array.Empty<string>()
                },
                modules = moduleDtos,
                edges = dependencyEdges,
                relationEdges,
                containmentEdges,
                collaborationEdges,
                crossWorks = crossWorkDtos,
                disciplines = disciplineDtos,
                depMap = topo.DepMap,
                rdepMap = topo.RdepMap,
                summary = $"Nodes {topo.Nodes.Count}, groups {groupCount}, teams {teamCount}, dependencies {dependencyEdges.Count}, containment {containmentEdges.Count}, collaboration {collaborationEdges.Count}",
                scannedAt = topo.BuiltAt
            });
        });

        api.MapGet("/plan", ([FromQuery] string modules, [FromServices] ITopoGraphApplicationService topology) =>
        {
            topology.BuildTopology();
            var names = modules
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var plan = topology.GetExecutionPlan(names);
            return Results.Json(new
            {
                plan.OrderedModules,
                plan.HasCycle,
                plan.CycleDescription,
                executionOrder = string.Join(" -> ", plan.OrderedModules)
            }, JsonOpts);
        });

        api.MapPost("/reload", ([FromServices] ITopoGraphApplicationService topology) =>
        {
            topology.ReloadManifests();
            var topo = topology.BuildTopology();
            return Results.Json(new
            {
                success = true,
                message = $"Reloaded {topo.Nodes.Count} nodes",
                moduleCount = topo.Nodes.Count
            });
        });
    }

    private static ModuleDto ToModuleDto(
        KnowledgeNode node,
        List<KnowledgeNode> allNodes,
        Dictionary<string, string> disciplineNames)
    {
        var sameNameCount = allNodes.Count(o => o.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));
        var key = sameNameCount > 1 ? (node.RelativePath ?? node.Name).Replace('\\', '/') : node.Name;
        var disciplineId = string.IsNullOrWhiteSpace(node.Discipline) ? "root" : node.Discipline!;
        var typeName = ResolveNodeTypeName(node);
        var workflow = ParseGovernanceList(node.Metadata, "workflow", "workflows", "process", "workDefinition", "mode");
        var rules = MergeUnique(node.Constraints, ParseGovernanceList(node.Metadata, "rules", "rule", "constraints"));
        var prohibitions = ParseGovernanceList(node.Metadata, "prohibitions", "forbidden", "forbid", "cannot");
        var publicApi = MergeUnique(node.PublicApi, ParseGovernanceList(node.Metadata, "publicApi", "publicApis", "interfaces", "capabilities"));
        var managedPathScopes = BuildManagedPathScopes(node, typeName);
        var fileAuthority = string.Equals(typeName, "Team", StringComparison.OrdinalIgnoreCase) ? "execute" : "govern";

        return new ModuleDto
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
            TypeLabel = ResolveNodeTypeLabel(typeName),
            Summary = node.Summary,
            Keywords = node.Keywords,
            Dependencies = node.Dependencies,
            ComputedDependencies = node.ComputedDependencies,
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
            Metadata = node.Metadata,
            ParentId = node.ParentId,
            ChildIds = node.ChildIds,
            ParentModuleId = node.ParentId,
            IsCrossWorkModule = node.IsCrossWorkModule
        };
    }

    private static List<RelationEdgeDto> BuildContainmentEdges(
        TopologySnapshot topo,
        List<ModuleDto> modules,
        Dictionary<string, string> disciplineNames,
        IReadOnlyDictionary<string, ModuleDto> modulesByNodeId)
    {
        var edges = new List<RelationEdgeDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        BuildProjectContainmentEdges(modules, disciplineNames, edges, seen);

        foreach (var relation in topo.ContainmentRelations)
        {
            var from = ResolveRelationEndpointKey(relation.FromId, modulesByNodeId);
            var to = ResolveRelationEndpointKey(relation.ToId, modulesByNodeId);
            TryAddContainmentEdge(
                from,
                to,
                string.IsNullOrWhiteSpace(relation.Label) ? "composition" : relation.Label,
                relation.IsComputed,
                edges,
                seen);
        }

        return edges;
    }

    private static List<RelationEdgeDto> BuildRelationEdges(
        IEnumerable<TopologyRelation> relations,
        IReadOnlyDictionary<string, ModuleDto> modulesByNodeId,
        string relationName)
    {
        var edges = new List<RelationEdgeDto>();
        foreach (var relation in relations)
        {
            var from = ResolveRelationEndpointKey(relation.FromId, modulesByNodeId);
            var to = ResolveRelationEndpointKey(relation.ToId, modulesByNodeId);
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                continue;

            edges.Add(new RelationEdgeDto
            {
                From = from,
                To = to,
                Relation = relationName,
                IsComputed = relation.IsComputed
            });
        }

        return edges;
    }

    private static void BuildProjectContainmentEdges(
        List<ModuleDto> modules,
        Dictionary<string, string> disciplineNames,
        List<RelationEdgeDto> edges,
        HashSet<string> seen)
    {
        var departmentIds = modules
            .Select(m => m.Discipline)
            .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, "root", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var disciplineId in disciplineNames.Keys
                     .Where(id => !string.IsNullOrWhiteSpace(id) && !string.Equals(id, "root", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(id => id, StringComparer.OrdinalIgnoreCase))
        {
            if (!departmentIds.Contains(disciplineId, StringComparer.OrdinalIgnoreCase))
                departmentIds.Add(disciplineId);
        }

        foreach (var disciplineId in departmentIds)
            TryAddContainmentEdge("project", ToDepartmentNodeId(disciplineId), "composition", false, edges, seen);

        foreach (var module in modules.Where(m => string.IsNullOrWhiteSpace(m.ParentId)))
        {
            var parentNode = string.Equals(module.Discipline, "root", StringComparison.OrdinalIgnoreCase)
                ? "project"
                : ToDepartmentNodeId(module.Discipline);

            TryAddContainmentEdge(parentNode, module.Name, "composition", false, edges, seen);
        }
    }

    private static void TryAddContainmentEdge(
        string from,
        string to,
        string kind,
        bool isComputed,
        List<RelationEdgeDto> edges,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return;
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;

        var key = $"{from}|{to}";
        if (!seen.Add(key)) return;

        edges.Add(new RelationEdgeDto
        {
            From = from,
            To = to,
            Relation = "containment",
            Kind = kind,
            IsComputed = isComputed
        });
    }

    private static List<CrossWorkDto> BuildCrossWorkDtos(List<CrossWork> crossWorks, List<ModuleDto> modules)
    {
        var moduleByName = modules.ToDictionary(m => m.DisplayName, m => m.Name, StringComparer.OrdinalIgnoreCase);
        return crossWorks.Select(cw => new CrossWorkDto
        {
            Id = cw.Id,
            Name = cw.Name,
            Description = cw.Description,
            Feature = cw.Feature,
            Participants = cw.Participants.Select(p => new CrossWorkParticipantDto
            {
                ModuleName = p.ModuleName,
                ModuleId = moduleByName.GetValueOrDefault(p.ModuleName),
                Role = p.Role,
                Contract = p.Contract,
                ContractType = p.ContractType,
                Deliverable = p.Deliverable
            }).ToList()
        }).ToList();
    }

    private static List<DisciplineDto> BuildDisciplineDtos(
        TopologyManagementSnapshot management,
        Dictionary<string, string> disciplineNames,
        List<ModuleDto> modules,
        List<CrossWork> crossWorks)
    {
        var groups = modules.Where(m => !IsTeam(m)).ToList();
        var knownIds = new HashSet<string>(FixedDepartmentIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in disciplineNames.Keys.Where(id => !string.Equals(id, "root", StringComparison.OrdinalIgnoreCase)))
            knownIds.Add(id);
        foreach (var module in groups.Where(m => !string.Equals(m.Discipline, "root", StringComparison.OrdinalIgnoreCase)))
            knownIds.Add(module.Discipline);

        var list = new List<DisciplineDto>();
        foreach (var id in knownIds.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            var def = management.Disciplines.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            var moduleNames = groups
                .Where(m => string.Equals(m.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayNameSet = groups
                .Where(m => string.Equals(m.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var crossWorkCount = crossWorks.Count(cw => cw.Participants.Any(p => displayNameSet.Contains(p.ModuleName)));

            list.Add(new DisciplineDto
            {
                Id = id,
                DisplayName = disciplineNames.GetValueOrDefault(id, id),
                RoleId = def?.RoleId,
                ModuleCount = moduleNames.Count,
                Modules = moduleNames,
                CrossWorkCount = crossWorkCount
            });
        }

        return list;
    }

    private static Dictionary<string, string> BuildDisciplineDisplayNames(TopologyManagementSnapshot management, List<KnowledgeNode> nodes)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var discipline in management.Disciplines)
        {
            if (string.IsNullOrWhiteSpace(discipline.Id)) continue;
            map[discipline.Id] = string.IsNullOrWhiteSpace(discipline.DisplayName)
                ? discipline.Id
                : discipline.DisplayName.Trim();
        }

        foreach (var node in nodes)
        {
            var id = string.IsNullOrWhiteSpace(node.Discipline) ? "root" : node.Discipline!;
            if (!map.ContainsKey(id))
                map[id] = GuessDisciplineDisplayName(id);
        }

        if (!map.ContainsKey("root"))
            map["root"] = "root";

        return map;
    }

    private static bool IsTeam(ModuleDto module)
        => string.Equals(module.TypeName, "Team", StringComparison.OrdinalIgnoreCase) || module.IsCrossWorkModule;

    private static string ResolveNodeTypeName(KnowledgeNode node)
        => node.IsCrossWorkModule
            ? "Team"
            : node.Type switch
            {
                NodeType.Project => "Project",
                NodeType.Department => "Department",
                NodeType.Team => "Team",
                _ => "Technical"
            };

    private static string ResolveNodeTypeLabel(string typeName)
        => typeName switch
        {
            "Project" => "Project",
            "Department" => "Department",
            "Team" => "Team",
            _ => "Technical"
        };

    private static List<string> BuildManagedPathScopes(KnowledgeNode node, string typeName)
    {
        if (node.ManagedPathScopes.Count > 0)
            return MergeUnique(node.ManagedPathScopes);

        var metadataScopes = ParseGovernanceList(node.Metadata, "managedPathScopes", "pathScopes", "managedPaths");
        var normalizedPath = NormalizePath(node.RelativePath);
        var baseScopes = new List<string>();
        if (!string.IsNullOrWhiteSpace(normalizedPath))
            baseScopes.Add(normalizedPath);
        if (string.Equals(typeName, "Team", StringComparison.OrdinalIgnoreCase))
            return metadataScopes;

        return MergeUnique(baseScopes, metadataScopes);
    }

    private static List<string> ParseGovernanceList(Dictionary<string, string>? metadata, params string[] keys)
    {
        if (metadata == null || metadata.Count == 0 || keys.Length == 0)
            return [];

        var values = new List<string>();
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            values.AddRange(ParseLooseList(raw));
        }

        return MergeUnique(values);
    }

    private static List<string> ParseLooseList(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var text = raw.Trim();
        if (text.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<List<string>>(text, JsonOpts);
                if (arr != null && arr.Count > 0)
                    return MergeUnique(arr);
            }
            catch
            {
                // Fall back to delimiter parsing.
            }
        }

        var parts = text.Split(['\n', ';', '|', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length <= 1
            ? MergeUnique([CleanListItem(text)])
            : MergeUnique(parts.Select(CleanListItem));
    }

    private static string CleanListItem(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var value = input.Trim();
        while (value.StartsWith("- ", StringComparison.Ordinal) || value.StartsWith("* ", StringComparison.Ordinal))
            value = value[2..].TrimStart();
        return value.Trim();
    }

    private static List<string> MergeUnique(params IEnumerable<string>?[] sources)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (source == null) continue;
            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                var normalized = item.Trim();
                if (normalized.Length == 0) continue;
                if (seen.Add(normalized))
                    result.Add(normalized);
            }
        }

        return result;
    }

    private static string ResolveNodeKey(string nameOrPath, List<ModuleDto> modules)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath)) return string.Empty;
        var exact = modules.FirstOrDefault(m => string.Equals(m.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase));
        return exact?.Name ?? nameOrPath;
    }

    private static string ResolveRelationEndpointKey(
        string relationNodeId,
        IReadOnlyDictionary<string, ModuleDto> modulesByNodeId)
    {
        if (string.IsNullOrWhiteSpace(relationNodeId))
            return string.Empty;

        return modulesByNodeId.TryGetValue(relationNodeId, out var module)
            ? module.Name
            : relationNodeId;
    }

    private static string ToDepartmentNodeId(string disciplineId) => $"__dept__:{disciplineId}";

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }

    private static string GuessDisciplineDisplayName(string disciplineId)
        => disciplineId switch
        {
            "product-design" => "product-design",
            "engineering" => "engineering",
            "devops" => "devops",
            "root" => "root",
            _ => disciplineId
        };

    private sealed class ModuleDto
    {
        public string Name { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string NodeId { get; init; } = string.Empty;
        public string? RelativePath { get; init; }
        public int Layer { get; init; }
        public string Discipline { get; init; } = "root";
        public string DisciplineDisplayName { get; init; } = "root";
        public string Type { get; init; } = "Technical";
        public string TypeName { get; init; } = "Technical";
        public string TypeLabel { get; init; } = "Technical";
        public string? Summary { get; init; }
        public List<string> Keywords { get; init; } = [];
        public List<string> Dependencies { get; init; } = [];
        public List<string> ComputedDependencies { get; init; } = [];
        public string? Maintainer { get; init; }
        public string? Boundary { get; init; }
        public string? Contract { get; init; }
        public List<string> PublicApi { get; init; } = [];
        public List<string> Constraints { get; init; } = [];
        public List<string> Workflow { get; init; } = [];
        public List<string> Rules { get; init; } = [];
        public List<string> Prohibitions { get; init; } = [];
        public string FileAuthority { get; init; } = "govern";
        public List<string> ManagedPathScopes { get; init; } = [];
        public Dictionary<string, string>? Metadata { get; init; }
        public string? ParentId { get; init; }
        public string? ParentModuleId { get; init; }
        public List<string> ChildIds { get; init; } = [];
        public bool IsCrossWorkModule { get; init; }
    }

    private sealed class RelationEdgeDto
    {
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string Relation { get; set; } = "dependency";
        public string? Kind { get; set; }
        public bool IsComputed { get; set; }
    }

    private sealed class CrossWorkDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? Feature { get; init; }
        public List<CrossWorkParticipantDto> Participants { get; init; } = [];
    }

    private sealed class CrossWorkParticipantDto
    {
        public string ModuleName { get; init; } = string.Empty;
        public string? ModuleId { get; init; }
        public string Role { get; init; } = string.Empty;
        public string? Contract { get; init; }
        public string? ContractType { get; init; }
        public string? Deliverable { get; init; }
    }

    private sealed class DisciplineDto
    {
        public string Id { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string? RoleId { get; init; }
        public int ModuleCount { get; init; }
        public List<string> Modules { get; init; } = [];
        public int CrossWorkCount { get; init; }
    }
}
