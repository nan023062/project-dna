using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Auth;
using Dna.Knowledge;
using Dna.Knowledge.Project.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dna.Interfaces.Api;

public static class TopologyEndpoints
{
    private static readonly string[] FixedDepartmentIds = ["product-design", "engineering", "devops"];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void MapTopologyEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.RequireAuthorization(ServerPolicies.ViewerOrAbove);

        api.MapGet("/topology", (IGraphEngine graph) =>
        {
            var topo = graph.BuildTopology();
            var arch = graph.GetArchitecture();
            var disciplineNames = BuildDisciplineDisplayNames(arch, topo.Nodes);

            var moduleDtos = topo.Nodes
                .Select(n => ToModuleDto(n, topo.Nodes, disciplineNames))
                .ToList();

            var dependencyEdges = topo.Edges
                .Select(e => new RelationEdgeDto
                {
                    From = ResolveNodeKey(e.From, moduleDtos),
                    To = ResolveNodeKey(e.To, moduleDtos),
                    Relation = "dependency",
                    IsComputed = e.IsComputed
                })
                .Where(e => !string.IsNullOrWhiteSpace(e.From) && !string.IsNullOrWhiteSpace(e.To))
                .ToList();

            var containmentEdges = BuildContainmentEdges(moduleDtos, disciplineNames);
            var collaborationEdges = BuildCollaborationEdges(moduleDtos, topo.CrossWorks);

            var relationEdges = dependencyEdges
                .Concat(containmentEdges)
                .Concat(collaborationEdges)
                .ToList();

            var disciplineDtos = BuildDisciplineDtos(arch, disciplineNames, moduleDtos, topo.CrossWorks);
            var crossWorkDtos = BuildCrossWorkDtos(topo.CrossWorks, moduleDtos);
            var teamCount = moduleDtos.Count(IsTeam);
            var groupCount = moduleDtos.Count - teamCount;

            return Results.Json(new
            {
                project = new
                {
                    id = "project",
                    name = "Project",
                    type = "Project",
                    typeName = "Project",
                    typeLabel = "项目",
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
                summary = $"共 {topo.Nodes.Count} 个图节点（技术组 {groupCount} · 执行团队 {teamCount}），依赖 {dependencyEdges.Count} · 包含 {containmentEdges.Count} · 协作 {collaborationEdges.Count} · {topo.BuiltAt:yyyy-MM-dd HH:mm}",
                scannedAt = topo.BuiltAt
            });
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        api.MapGet("/plan", (
            [FromQuery] string modules,
            IGraphEngine graph) =>
        {
            graph.BuildTopology();
            var names = modules
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            var plan = graph.GetExecutionPlan(names);
            return Results.Json(new
            {
                plan.OrderedModules,
                plan.HasCycle,
                plan.CycleDescription,
                executionOrder = string.Join(" → ", plan.OrderedModules)
            }, JsonOpts);
        }).RequireAuthorization(ServerPolicies.ViewerOrAbove);

        api.MapPost("/reload", (IGraphEngine graph) =>
        {
            graph.ReloadManifests();
            var topo = graph.BuildTopology();
            return Results.Json(new
            {
                success = true,
                message = $"已重载，{topo.Nodes.Count} 个图节点",
                moduleCount = topo.Nodes.Count
            });
        }).RequireAuthorization(ServerPolicies.AdminOnly);
    }

    private static ModuleDto ToModuleDto(
        KnowledgeNode node,
        List<KnowledgeNode> allNodes,
        Dictionary<string, string> disciplineNames)
    {
        var sameNameCount = allNodes.Count(o =>
            o.Name.Equals(node.Name, StringComparison.OrdinalIgnoreCase));
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
        List<ModuleDto> modules,
        Dictionary<string, string> disciplineNames)
    {
        var edges = new List<RelationEdgeDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        BuildProjectContainmentEdges(modules, disciplineNames, edges, seen);
        BuildExplicitContainmentEdges(modules, edges, seen);

        return edges;
    }

    private static void BuildExplicitContainmentEdges(
        List<ModuleDto> modules,
        List<RelationEdgeDto> edges,
        HashSet<string> seen)
    {
        var byNodeId = modules
            .Where(m => !string.IsNullOrWhiteSpace(m.NodeId))
            .ToDictionary(m => m.NodeId, m => m, StringComparer.OrdinalIgnoreCase);

        foreach (var child in modules)
        {
            if (string.IsNullOrWhiteSpace(child.ParentId)) continue;
            if (!byNodeId.TryGetValue(child.ParentId, out var parent)) continue;
            TryAddContainmentEdge(
                from: parent.Name,
                to: child.Name,
                kind: "composition",
                isComputed: false,
                edges,
                seen);
        }

        foreach (var parent in modules)
        {
            if (parent.ChildIds == null || parent.ChildIds.Count == 0) continue;
            foreach (var childId in parent.ChildIds)
            {
                if (string.IsNullOrWhiteSpace(childId)) continue;
                if (!byNodeId.TryGetValue(childId, out var child)) continue;
                TryAddContainmentEdge(
                    from: parent.Name,
                    to: child.Name,
                    kind: "composition",
                    isComputed: false,
                    edges,
                    seen);
            }
        }
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
        {
            TryAddContainmentEdge(
                from: "project",
                to: ToDepartmentNodeId(disciplineId),
                kind: "composition",
                isComputed: false,
                edges,
                seen);
        }

        foreach (var module in modules.Where(m => string.IsNullOrWhiteSpace(m.ParentId)))
        {
            var parentNode = string.Equals(module.Discipline, "root", StringComparison.OrdinalIgnoreCase)
                ? "project"
                : ToDepartmentNodeId(module.Discipline);

            TryAddContainmentEdge(
                from: parentNode,
                to: module.Name,
                kind: "composition",
                isComputed: false,
                edges,
                seen);
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

    private static List<RelationEdgeDto> BuildCollaborationEdges(List<ModuleDto> modules, List<CrossWork> crossWorks)
    {
        var moduleByName = modules.ToDictionary(m => m.DisplayName, m => m, StringComparer.OrdinalIgnoreCase);
        var merged = new Dictionary<string, RelationEdgeDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var cw in crossWorks)
        {
            var participants = cw.Participants
                .Select(p => moduleByName.GetValueOrDefault(p.ModuleName)?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (participants.Count < 2) continue;
            var crossWorkNode = moduleByName.GetValueOrDefault(cw.Name)?.Name;

            if (!string.IsNullOrWhiteSpace(crossWorkNode))
            {
                foreach (var participant in participants)
                {
                    if (string.Equals(crossWorkNode, participant, StringComparison.OrdinalIgnoreCase)) continue;
                    MergeCollaborationEdge(merged, crossWorkNode!, participant!, cw.Id, cw.Name);
                }
                continue;
            }

            for (var i = 0; i < participants.Count; i++)
            {
                for (var j = i + 1; j < participants.Count; j++)
                    MergeCollaborationEdge(merged, participants[i]!, participants[j]!, cw.Id, cw.Name);
            }
        }

        return merged.Values.ToList();
    }

    private static void MergeCollaborationEdge(
        Dictionary<string, RelationEdgeDto> merged,
        string left,
        string right,
        string? crossWorkId,
        string? crossWorkName)
    {
        var (from, to) = string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0
            ? (left, right)
            : (right, left);

        var key = $"{from}|{to}";
        if (!merged.TryGetValue(key, out var edge))
        {
            edge = new RelationEdgeDto
            {
                From = from,
                To = to,
                Relation = "collaboration",
                IsComputed = true,
                CrossWorkIds = [],
                CrossWorkNames = []
            };
            merged[key] = edge;
        }

        if (!string.IsNullOrWhiteSpace(crossWorkId) &&
            !edge.CrossWorkIds!.Contains(crossWorkId, StringComparer.OrdinalIgnoreCase))
        {
            edge.CrossWorkIds!.Add(crossWorkId);
        }

        if (!string.IsNullOrWhiteSpace(crossWorkName) &&
            !edge.CrossWorkNames!.Contains(crossWorkName, StringComparer.OrdinalIgnoreCase))
        {
            edge.CrossWorkNames!.Add(crossWorkName);
        }
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
        ArchitectureManifest arch,
        Dictionary<string, string> disciplineNames,
        List<ModuleDto> modules,
        List<CrossWork> crossWorks)
    {
        var groups = modules
            .Where(m => !IsTeam(m))
            .ToList();

        var knownIds = new HashSet<string>(FixedDepartmentIds, StringComparer.OrdinalIgnoreCase);
        foreach (var id in disciplineNames.Keys.Where(id => !string.Equals(id, "root", StringComparison.OrdinalIgnoreCase)))
            knownIds.Add(id);
        foreach (var m in groups.Where(m => !string.Equals(m.Discipline, "root", StringComparison.OrdinalIgnoreCase)))
            knownIds.Add(m.Discipline);

        var list = new List<DisciplineDto>();
        foreach (var id in knownIds.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
        {
            arch.Disciplines.TryGetValue(id, out var def);
            var moduleNames = groups
                .Where(m => string.Equals(m.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var displayNameSet = groups
                .Where(m => string.Equals(m.Discipline, id, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.DisplayName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var crossWorkCount = crossWorks.Count(cw =>
                cw.Participants.Any(p =>
                    displayNameSet.Contains(p.ModuleName)));

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

    private static Dictionary<string, string> BuildDisciplineDisplayNames(
        ArchitectureManifest arch,
        List<KnowledgeNode> nodes)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, def) in arch.Disciplines)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var display = string.IsNullOrWhiteSpace(def.DisplayName) ? id : def.DisplayName.Trim();
            if (string.Equals(id, "root", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(display, "root", StringComparison.OrdinalIgnoreCase))
            {
                display = "执行团队";
            }
            map[id] = display;
        }

        foreach (var node in nodes)
        {
            var id = string.IsNullOrWhiteSpace(node.Discipline) ? "root" : node.Discipline!;
            if (map.ContainsKey(id)) continue;
            map[id] = GuessDisciplineDisplayName(id);
        }

        if (!map.ContainsKey("root"))
            map["root"] = "执行团队";

        return map;
    }

    private static bool IsTeam(ModuleDto module)
    {
        return string.Equals(module.TypeName, "Team", StringComparison.OrdinalIgnoreCase)
               || module.IsCrossWorkModule;
    }

    private static string ResolveNodeTypeName(KnowledgeNode node)
    {
        if (node.IsCrossWorkModule) return "Team";

        return node.Type switch
        {
            NodeType.Project => "Project",
            NodeType.Department => "Department",
            _ => "Technical"
        };
    }

    private static string ResolveNodeTypeLabel(string typeName)
    {
        return typeName switch
        {
            "Project" => "项目",
            "Department" => "部门",
            "Team" => "执行团队",
            _ => "技术组"
        };
    }

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
                // Ignore malformed json list, fallback to delimiter parsing.
            }
        }

        var parts = text.Split(
            ['\n', ';', '|', ',', '；', '，', '、'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length <= 1)
            return MergeUnique([CleanListItem(text)]);

        return MergeUnique(parts.Select(CleanListItem));
    }

    private static string CleanListItem(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var value = input.Trim();

        while (value.StartsWith("- ", StringComparison.Ordinal) ||
               value.StartsWith("* ", StringComparison.Ordinal) ||
               value.StartsWith("• ", StringComparison.Ordinal))
        {
            value = value.Substring(2).TrimStart();
        }

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
        var exact = modules.FirstOrDefault(m =>
            string.Equals(m.DisplayName, nameOrPath, StringComparison.OrdinalIgnoreCase));
        return exact?.Name ?? nameOrPath;
    }

    private static string ToDepartmentNodeId(string disciplineId)
        => $"__dept__:{disciplineId}";

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').Trim();
        normalized = normalized.Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsPathParent(string parentPath, string childPath)
    {
        if (parentPath.Length >= childPath.Length) return false;
        return childPath.StartsWith(parentPath + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessDisciplineDisplayName(string disciplineId)
    {
        return disciplineId switch
        {
            "product-design" => "产品组",
            "engineering" => "程序组",
            "devops" => "运维组",
            "root" => "执行团队",
            _ => disciplineId
        };
    }

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
        public string TypeLabel { get; init; } = "技术组";
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
        public List<string>? CrossWorkIds { get; set; }
        public List<string>? CrossWorkNames { get; set; }
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
