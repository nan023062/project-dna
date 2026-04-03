using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using TopologyKnowledgeSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyKnowledgeSummary;
using TopologyLessonSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyLessonSummary;
using TopologyModuleContractModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModuleContract;
using TopologyModulePathBindingModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModulePathBinding;

namespace Dna.Knowledge;

public sealed partial class TopoGraphApplicationService
{
    private void RegisterModuleCore(string discipline, TopologyModuleDefinition module)
    {
        var normalizedDiscipline = NormalizeDisciplineId(discipline);
        var definition = CloneDefinition(_facade.GetDefinition());
        var disciplineView = BuildManagementSnapshot(_facade.GetSnapshot()).Disciplines.FirstOrDefault(item =>
            string.Equals(item.Id, normalizedDiscipline, StringComparison.OrdinalIgnoreCase));
        var department = EnsureDepartment(
            ref definition,
            normalizedDiscipline,
            disciplineView?.DisplayName ?? normalizedDiscipline,
            disciplineView?.RoleId ?? "coder",
            disciplineView?.Layers ?? []);
        var existing = FindModuleRegistration(definition, module.Id, module.Name);
        var moduleKind = InferModuleKind(module, existing);
        var parentId = ResolveParentId(definition, module.ParentModuleId, department.Id);
        var moduleId = ResolveModuleId(existing, module.Id, parentId, module.Name);
        var dependencies = ResolveDependencyTargets(definition, module.Dependencies);
        var participants = NormalizeParticipants(module.Participants);
        var collaborationIds = ResolveParticipantNodeIds(definition, participants);
        var metadata = MergeMetadata(existing, module.Metadata);
        var knowledge = new TopologyKnowledgeSummaryModel
        {
            Identity = BuildModuleIdentityMarkdown(module, existing)
        };

        RemoveModule(ref definition, module.Id, module.Name);

        if (moduleKind == TopologyNodeKind.Team)
        {
            var existingTeam = existing as TeamNodeRegistration;
            definition.TeamNodes.Add(new TeamNodeRegistration
            {
                Id = moduleId,
                Name = module.Name.Trim(),
                ParentId = parentId,
                Summary = FirstNonEmpty(module.Summary, existingTeam?.Summary),
                Maintainer = FirstNonEmpty(module.Maintainer, existingTeam?.Maintainer),
                Layer = Math.Max(module.Layer, 0),
                IsCrossWorkModule = module.IsCrossWorkModule,
                Participants = participants,
                Metadata = metadata,
                PathBinding = BuildPathBinding(module),
                BusinessObjective = FirstNonEmpty(module.Summary, existingTeam?.BusinessObjective),
                Deliverables = participants
                    .Select(item => item.Deliverable)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                TechnicalDependencies = dependencies,
                CollaborationIds = collaborationIds,
                Knowledge = knowledge
            });
        }
        else
        {
            var existingTechnical = existing as TechnicalNodeRegistration;
            definition.TechnicalNodes.Add(new TechnicalNodeRegistration
            {
                Id = moduleId,
                Name = module.Name.Trim(),
                ParentId = parentId,
                Summary = FirstNonEmpty(module.Summary, existingTechnical?.Summary),
                Maintainer = FirstNonEmpty(module.Maintainer, existingTechnical?.Maintainer),
                Layer = Math.Max(module.Layer, 0),
                IsCrossWorkModule = false,
                Participants = [],
                Metadata = metadata,
                PathBinding = BuildPathBinding(module),
                CapabilityTags = existingTechnical?.CapabilityTags ?? [],
                DeclaredDependencies = dependencies,
                ComputedDependencies = existingTechnical?.ComputedDependencies ?? [],
                Contract = new TopologyModuleContractModel
                {
                    Boundary = FirstNonEmpty(module.Boundary, existingTechnical?.Contract.Boundary),
                    PublicApi = module.PublicApi?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? existingTechnical?.Contract.PublicApi ?? [],
                    Constraints = module.Constraints?.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? existingTechnical?.Contract.Constraints ?? []
                },
                Knowledge = knowledge
            });
        }

        module.Id = moduleId;
        module.ParentModuleId = parentId;

        _facade.ReplaceDefinition(definition);
        _store.Reload();
        InvalidateTopologyCacheLocked();
    }

    private void SaveCrossWorkCore(TopologyCrossWorkDefinition crossWork)
    {
        if (string.IsNullOrWhiteSpace(crossWork.Name))
            throw new InvalidOperationException("crosswork.name 不能为空");

        var ownership = ComputeCrossWorkOwnership(crossWork.Participants);
        RegisterModuleCore(ownership.discipline, new TopologyModuleDefinition
        {
            Id = crossWork.Id,
            Name = crossWork.Name,
            Path = string.Empty,
            Layer = ownership.layer,
            IsCrossWorkModule = true,
            Participants = NormalizeParticipants(crossWork.Participants),
            Summary = crossWork.Description,
            Metadata = BuildCrossWorkMetadata(crossWork)
        });
    }

    private bool UnregisterModuleCore(string name)
    {
        var definition = CloneDefinition(_facade.GetDefinition());
        if (!RemoveModule(ref definition, name, name))
            return false;

        _facade.ReplaceDefinition(definition);
        _store.Reload();
        InvalidateTopologyCacheLocked();
        return true;
    }

    private bool RemoveCrossWorkCore(string crossWorkId)
    {
        var definition = CloneDefinition(_facade.GetDefinition());
        var target = definition.TeamNodes.FirstOrDefault(item =>
            item.IsCrossWorkModule &&
            (string.Equals(item.Id, crossWorkId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(item.Name, crossWorkId, StringComparison.OrdinalIgnoreCase)));
        if (target == null)
            return false;

        definition.TeamNodes.RemoveAll(item => string.Equals(item.Id, target.Id, StringComparison.OrdinalIgnoreCase));
        _facade.ReplaceDefinition(definition);
        _store.Reload();
        InvalidateTopologyCacheLocked();
        return true;
    }

    private void UpsertDisciplineCore(string disciplineId, string? displayName, string roleId, List<LayerDefinition> layers)
    {
        var normalizedDiscipline = NormalizeDisciplineId(disciplineId);
        var definition = CloneDefinition(_facade.GetDefinition());
        EnsureDepartment(
            ref definition,
            normalizedDiscipline,
            FirstNonEmpty(displayName, normalizedDiscipline)!,
            roleId,
            layers);

        _facade.ReplaceDefinition(definition);
        _store.Reload();
        InvalidateTopologyCacheLocked();
    }

    private bool RemoveDisciplineCore(string disciplineId)
    {
        var normalizedDiscipline = NormalizeDisciplineId(disciplineId);
        var definition = CloneDefinition(_facade.GetDefinition());
        var removedFromFiles = RemoveDepartment(ref definition, normalizedDiscipline);
        if (!removedFromFiles)
            return false;

        _facade.ReplaceDefinition(definition);
        _store.Reload();
        InvalidateTopologyCacheLocked();
        return true;
    }

    private static TopologyModelDefinition CloneDefinition(TopologyModelDefinition definition)
    {
        return new TopologyModelDefinition
        {
            Project = definition.Project == null ? null : new ProjectNodeRegistration
            {
                Id = definition.Project.Id,
                Name = definition.Project.Name,
                Summary = definition.Project.Summary,
                ParentId = definition.Project.ParentId,
                Vision = definition.Project.Vision,
                WorkspaceRoot = definition.Project.WorkspaceRoot,
                Steward = definition.Project.Steward,
                ExcludeDirs = [.. definition.Project.ExcludeDirs],
                Metadata = new Dictionary<string, string>(definition.Project.Metadata, StringComparer.OrdinalIgnoreCase),
                Knowledge = CloneKnowledge(definition.Project.Knowledge)
            },
            Departments = definition.Departments.Select(department => new DepartmentNodeRegistration
            {
                Id = department.Id,
                Name = department.Name,
                Summary = department.Summary,
                ParentId = department.ParentId,
                DisciplineCode = department.DisciplineCode,
                Scope = department.Scope,
                Owner = department.Owner,
                RoleId = department.RoleId,
                Layers = department.Layers.Select(layer => new LayerDefinition
                {
                    Level = layer.Level,
                    Name = layer.Name
                }).ToList(),
                Metadata = new Dictionary<string, string>(department.Metadata, StringComparer.OrdinalIgnoreCase),
                Knowledge = CloneKnowledge(department.Knowledge)
            }).ToList(),
            TechnicalNodes = definition.TechnicalNodes.Select(technical => new TechnicalNodeRegistration
            {
                Id = technical.Id,
                Name = technical.Name,
                Summary = technical.Summary,
                ParentId = technical.ParentId,
                Maintainer = technical.Maintainer,
                Layer = technical.Layer,
                IsCrossWorkModule = technical.IsCrossWorkModule,
                Participants = technical.Participants.Select(CloneParticipant).ToList(),
                Metadata = new Dictionary<string, string>(technical.Metadata, StringComparer.OrdinalIgnoreCase),
                PathBinding = new TopologyModulePathBindingModel
                {
                    MainPath = technical.PathBinding.MainPath,
                    ManagedPaths = [.. technical.PathBinding.ManagedPaths]
                },
                Contract = new TopologyModuleContractModel
                {
                    Boundary = technical.Contract.Boundary,
                    PublicApi = [.. technical.Contract.PublicApi],
                    Constraints = [.. technical.Contract.Constraints]
                },
                DeclaredDependencies = [.. technical.DeclaredDependencies],
                ComputedDependencies = [.. technical.ComputedDependencies],
                CapabilityTags = [.. technical.CapabilityTags],
                Knowledge = CloneKnowledge(technical.Knowledge)
            }).ToList(),
            TeamNodes = definition.TeamNodes.Select(team => new TeamNodeRegistration
            {
                Id = team.Id,
                Name = team.Name,
                Summary = team.Summary,
                ParentId = team.ParentId,
                Maintainer = team.Maintainer,
                Layer = team.Layer,
                IsCrossWorkModule = team.IsCrossWorkModule,
                Participants = team.Participants.Select(CloneParticipant).ToList(),
                Metadata = new Dictionary<string, string>(team.Metadata, StringComparer.OrdinalIgnoreCase),
                PathBinding = new TopologyModulePathBindingModel
                {
                    MainPath = team.PathBinding.MainPath,
                    ManagedPaths = [.. team.PathBinding.ManagedPaths]
                },
                BusinessObjective = team.BusinessObjective,
                TechnicalDependencies = [.. team.TechnicalDependencies],
                Deliverables = [.. team.Deliverables],
                CollaborationIds = [.. team.CollaborationIds],
                Knowledge = CloneKnowledge(team.Knowledge)
            }).ToList(),
            Collaborations = definition.Collaborations.Select(item => new CollaborationRegistration
            {
                FromId = item.FromId,
                ToId = item.ToId,
                Label = item.Label
            }).ToList()
        };
    }

    private static TopologyKnowledgeSummaryModel CloneKnowledge(TopologyKnowledgeSummaryModel knowledge)
    {
        return new TopologyKnowledgeSummaryModel
        {
            Identity = knowledge.Identity,
            Facts = [.. knowledge.Facts],
            MemoryIds = [.. knowledge.MemoryIds],
            Lessons = knowledge.Lessons.Select(item => new TopologyLessonSummaryModel
            {
                Title = item.Title,
                Severity = item.Severity,
                Resolution = item.Resolution
            }).ToList()
        };
    }

    private static TopologyCrossWorkParticipantDefinition CloneParticipant(TopologyCrossWorkParticipantDefinition participant)
    {
        return new TopologyCrossWorkParticipantDefinition
        {
            ModuleName = participant.ModuleName,
            Role = participant.Role,
            ContractType = participant.ContractType,
            Contract = participant.Contract,
            Deliverable = participant.Deliverable
        };
    }

    private static DepartmentNodeRegistration EnsureDepartment(
        ref TopologyModelDefinition definition,
        string disciplineId,
        string displayName,
        string roleId,
        List<LayerDefinition> layers)
    {
        var existing = definition.Departments.FirstOrDefault(department =>
            string.Equals(department.DisciplineCode, disciplineId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(department.Id, disciplineId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(department.Name, disciplineId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            definition.Departments.RemoveAll(item => string.Equals(item.Id, existing.Id, StringComparison.OrdinalIgnoreCase));
            var updated = new DepartmentNodeRegistration
            {
                Id = existing.Id,
                Name = string.IsNullOrWhiteSpace(displayName) ? existing.Name : displayName.Trim(),
                Summary = existing.Summary,
                ParentId = existing.ParentId,
                DisciplineCode = disciplineId,
                Scope = existing.Scope,
                Owner = existing.Owner,
                RoleId = string.IsNullOrWhiteSpace(roleId) ? existing.RoleId : roleId.Trim(),
                Layers = layers.Count > 0 ? NormalizeLayers(layers) : existing.Layers,
                Metadata = new Dictionary<string, string>(existing.Metadata, StringComparer.OrdinalIgnoreCase),
                Knowledge = CloneKnowledge(existing.Knowledge)
            };
            definition.Departments.Add(updated);
            return updated;
        }

        if (definition.Project == null)
        {
            definition = new TopologyModelDefinition
            {
                Project = new ProjectNodeRegistration
                {
                    Id = "Project",
                    Name = "Project"
                },
                Departments = definition.Departments,
                TechnicalNodes = definition.TechnicalNodes,
                TeamNodes = definition.TeamNodes,
                Collaborations = definition.Collaborations
            };
        }

        var department = new DepartmentNodeRegistration
        {
            Id = BuildChildUid(definition.Project!.Id, displayName),
            Name = displayName.Trim(),
            ParentId = definition.Project.Id,
            DisciplineCode = disciplineId,
            Scope = displayName,
            RoleId = string.IsNullOrWhiteSpace(roleId) ? "coder" : roleId.Trim(),
            Layers = NormalizeLayers(layers),
            Knowledge = new TopologyKnowledgeSummaryModel
            {
                Identity = BuildDepartmentIdentityMarkdown(displayName, disciplineId)
            }
        };

        definition.Departments.Add(department);
        return department;
    }

    private static List<LayerDefinition> NormalizeLayers(List<LayerDefinition> layers)
    {
        return layers
            .Where(layer => !string.IsNullOrWhiteSpace(layer.Name))
            .OrderBy(layer => layer.Level)
            .Select(layer => new LayerDefinition
            {
                Level = layer.Level,
                Name = layer.Name.Trim()
            })
            .ToList();
    }

    private static string ResolveParentId(TopologyModelDefinition definition, string? parentInput, string defaultParentId)
    {
        var resolved = ResolveExistingNodeId(definition, parentInput);
        return string.IsNullOrWhiteSpace(resolved) ? defaultParentId : resolved;
    }

    private static string ResolveModuleId(TopologyNodeRegistration? existing, string? requestedId, string parentId, string name)
    {
        if (existing != null && !string.IsNullOrWhiteSpace(existing.Id))
            return existing.Id;
        if (!string.IsNullOrWhiteSpace(requestedId))
            return requestedId.Trim();
        return BuildChildUid(parentId, name);
    }

    private static List<string> ResolveDependencyTargets(TopologyModelDefinition definition, List<string> dependencies)
    {
        return dependencies
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => ResolveExistingNodeId(definition, item) ?? item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> ResolveParticipantNodeIds(
        TopologyModelDefinition definition,
        List<TopologyCrossWorkParticipantDefinition> participants)
    {
        return participants
            .Select(item => ResolveExistingNodeId(definition, item.ModuleName) ?? item.ModuleName.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ResolveExistingNodeId(TopologyModelDefinition definition, string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;

        var input = idOrName.Trim();
        var node = definition.Departments.Cast<TopologyNodeRegistration>()
            .Concat(definition.TechnicalNodes)
            .Concat(definition.TeamNodes)
            .FirstOrDefault(item =>
                string.Equals(item.Id, input, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.Name, input, StringComparison.OrdinalIgnoreCase));

        return node?.Id;
    }

    private static TopologyNodeRegistration? FindModuleRegistration(TopologyModelDefinition definition, string? moduleId, string moduleName)
    {
        return definition.TechnicalNodes.Cast<TopologyNodeRegistration>()
            .Concat(definition.TeamNodes)
            .FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(moduleId) &&
                 string.Equals(item.Id, moduleId.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                string.Equals(item.Name, moduleName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static TopologyNodeKind InferModuleKind(TopologyModuleDefinition module, TopologyNodeRegistration? existing)
    {
        return existing switch
        {
            TeamNodeRegistration => TopologyNodeKind.Team,
            TechnicalNodeRegistration => TopologyNodeKind.Technical,
            _ when module.IsCrossWorkModule => TopologyNodeKind.Team,
            _ when module.Layer >= 3 => TopologyNodeKind.Team,
            _ => TopologyNodeKind.Technical
        };
    }

    private static TopologyModulePathBindingModel BuildPathBinding(TopologyModuleDefinition module)
    {
        var managedPaths = new List<string>();
        AddPath(managedPaths, module.Path);
        if (module.ManagedPaths is { Count: > 0 })
        {
            foreach (var path in module.ManagedPaths)
                AddPath(managedPaths, path);
        }

        return new TopologyModulePathBindingModel
        {
            MainPath = NormalizePath(module.Path),
            ManagedPaths = managedPaths
        };
    }

    private static void AddPath(List<string> values, string? rawPath)
    {
        var normalized = NormalizePath(rawPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        if (!values.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            values.Add(normalized);
    }

    private static List<TopologyCrossWorkParticipantDefinition> NormalizeParticipants(List<TopologyCrossWorkParticipantDefinition>? participants)
    {
        if (participants is not { Count: > 0 })
            return [];

        return participants
            .Where(item => !string.IsNullOrWhiteSpace(item.ModuleName))
            .GroupBy(item => item.ModuleName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var first = group.First();
                return new TopologyCrossWorkParticipantDefinition
                {
                    ModuleName = first.ModuleName.Trim(),
                    Role = first.Role.Trim(),
                    ContractType = FirstNonEmpty(group.Select(item => item.ContractType).ToArray()),
                    Contract = FirstNonEmpty(group.Select(item => item.Contract).ToArray()),
                    Deliverable = FirstNonEmpty(group.Select(item => item.Deliverable).ToArray())
                };
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildCrossWorkMetadata(TopologyCrossWorkDefinition crossWork)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(crossWork.Feature))
            metadata["feature"] = crossWork.Feature.Trim();
        return metadata;
    }

    private (string discipline, int layer) ComputeCrossWorkOwnership(List<TopologyCrossWorkParticipantDefinition> participants)
    {
        if (participants is not { Count: > 0 })
            return ("root", 0);

        var management = BuildManagementSnapshot(_facade.GetSnapshot());
        var participantNames = new HashSet<string>(
            participants.Select(item => item.ModuleName),
            StringComparer.OrdinalIgnoreCase);
        var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxLayer = 0;

        foreach (var module in management.Modules)
        {
            if (!participantNames.Contains(module.Name))
                continue;

            disciplines.Add(module.Discipline);
            if (module.Layer > maxLayer)
                maxLayer = module.Layer;
        }

        return disciplines.Count == 1 ? (disciplines.First(), maxLayer) : ("root", 0);
    }

    private static Dictionary<string, string> MergeMetadata(TopologyNodeRegistration? existing, Dictionary<string, string>? overrides)
    {
        var result = existing switch
        {
            GroupNodeRegistration group => new Dictionary<string, string>(group.Metadata, StringComparer.OrdinalIgnoreCase),
            ModuleNodeRegistration module => new Dictionary<string, string>(module.Metadata, StringComparer.OrdinalIgnoreCase),
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        if (overrides == null)
            return result;

        foreach (var (key, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            result[key.Trim()] = value.Trim();
        }

        return result;
    }

    private static bool RemoveModule(ref TopologyModelDefinition definition, string? moduleId, string moduleName)
    {
        var existing = FindModuleRegistration(definition, moduleId, moduleName);
        if (existing == null)
            return false;

        definition.TechnicalNodes.RemoveAll(item => string.Equals(item.Id, existing.Id, StringComparison.OrdinalIgnoreCase));
        definition.TeamNodes.RemoveAll(item => string.Equals(item.Id, existing.Id, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static bool RemoveDepartment(ref TopologyModelDefinition definition, string disciplineId)
    {
        return definition.Departments.RemoveAll(department =>
            string.Equals(department.DisciplineCode, disciplineId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(department.Id, disciplineId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(department.Name, disciplineId, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    private static string BuildDepartmentIdentityMarkdown(string displayName, string disciplineId)
    {
        return string.Join(Environment.NewLine, new[]
        {
            "## Summary",
            string.Empty,
            $"{displayName} department for {disciplineId} modules.",
            string.Empty,
            "## Responsibility",
            string.Empty,
            $"- Govern the {disciplineId} boundary",
            "- Organize Technical and Team nodes in this discipline",
            "- Keep dependencies easy to review",
            string.Empty
        });
    }

    private static string BuildModuleIdentityMarkdown(TopologyModuleDefinition module, TopologyNodeRegistration? existing)
    {
        var summary = FirstNonEmpty(module.Summary, existing?.Summary, $"{module.Name.Trim()} module");
        var lines = new List<string>
        {
            "## Summary",
            string.Empty,
            summary!
        };

        if (module.PublicApi is { Count: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add("## Contract");
            lines.Add(string.Empty);
            if (!string.IsNullOrWhiteSpace(module.Boundary))
                lines.Add($"Boundary: {module.Boundary.Trim()}");
            foreach (var api in module.PublicApi.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"- {api.Trim()}");
        }

        if (module.Constraints is { Count: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add("## Constraints");
            lines.Add(string.Empty);
            foreach (var constraint in module.Constraints.Where(item => !string.IsNullOrWhiteSpace(item)))
                lines.Add($"- {constraint.Trim()}");
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string NormalizeDisciplineId(string disciplineId)
        => disciplineId.Trim().ToLowerInvariant();

    private static string BuildChildUid(string parentId, string name)
    {
        var segment = SanitizeUidSegment(name);
        return string.IsNullOrWhiteSpace(parentId) ? segment : $"{parentId}/{segment}";
    }

    private static string SanitizeUidSegment(string raw)
    {
        var chars = raw
            .Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_')
            .ToArray();
        return chars.Length == 0 ? "Module" : new string(chars);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }
}
