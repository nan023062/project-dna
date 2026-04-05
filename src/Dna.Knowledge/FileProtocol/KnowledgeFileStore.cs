using System.Text.Json;
using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Models.Nodes;
using Dna.Knowledge.TopoGraph.Models.Registrations;
using Dna.Knowledge.TopoGraph.Models.Snapshots;
using TopologyKnowledgeSummaryModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.TopologyKnowledgeSummary;
using TopologyModuleContractModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModuleContract;
using TopologyModulePathBindingModel = Dna.Knowledge.TopoGraph.Models.ValueObjects.ModulePathBinding;

namespace Dna.Knowledge.FileProtocol;

public sealed class KnowledgeFileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public List<ModuleFile> LoadModules(string agenticOsPath)
    {
        var modulesRoot = FileProtocolPaths.GetModulesRoot(agenticOsPath);
        if (!Directory.Exists(modulesRoot))
            return [];

        var results = new List<ModuleFile>();
        ScanModulesRecursive(modulesRoot, results);
        return results;
    }

    public ModuleFile? LoadModule(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetModuleFilePath(agenticOsPath, uid);
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<ModuleFile>(json, ReadOptions);
    }

    public string? LoadIdentity(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetIdentityFilePath(agenticOsPath, uid);
        return File.Exists(filePath) ? File.ReadAllText(filePath) : null;
    }

    public List<DependencyEntry> LoadDependencies(string agenticOsPath, string uid)
    {
        var filePath = FileProtocolPaths.GetDependenciesFilePath(agenticOsPath, uid);
        if (!File.Exists(filePath))
            return [];

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<DependencyEntry>>(json, ReadOptions) ?? [];
    }

    public TopologyModelDefinition LoadAsDefinition(string agenticOsPath)
    {
        var modules = LoadModules(agenticOsPath);
        ProjectNodeRegistration? project = null;
        var departments = new List<DepartmentNodeRegistration>();
        var technicals = new List<TechnicalNodeRegistration>();
        var teams = new List<TeamNodeRegistration>();

        foreach (var module in modules)
        {
            var identity = LoadIdentity(agenticOsPath, module.Uid);
            var deps = LoadDependencies(agenticOsPath, module.Uid);

            switch (module.Type)
            {
                case TopologyNodeKind.Project:
                    project = ToProjectRegistration(module, identity);
                    break;
                case TopologyNodeKind.Department:
                    departments.Add(ToDepartmentRegistration(module, identity));
                    break;
                case TopologyNodeKind.Technical:
                    technicals.Add(ToTechnicalRegistration(module, identity, deps));
                    break;
                case TopologyNodeKind.Team:
                    teams.Add(ToTeamRegistration(module, identity, deps));
                    break;
            }
        }

        return new TopologyModelDefinition
        {
            Project = project,
            Departments = departments,
            TechnicalNodes = technicals,
            TeamNodes = teams
        };
    }

    public void SaveModule(string agenticOsPath, ModuleFile module, string? identityMarkdown = null, List<DependencyEntry>? dependencies = null)
    {
        var moduleDir = FileProtocolPaths.GetModuleDir(agenticOsPath, module.Uid);
        Directory.CreateDirectory(moduleDir);
        var identityPath = Path.Combine(moduleDir, FileProtocolPaths.IdentityFileName);
        var dependenciesPath = Path.Combine(moduleDir, FileProtocolPaths.DependenciesFileName);

        SortArrays(module);
        var json = JsonSerializer.Serialize(module, JsonOptions);
        File.WriteAllText(Path.Combine(moduleDir, FileProtocolPaths.ModuleFileName), json + "\n");

        if (identityMarkdown != null)
            File.WriteAllText(identityPath, identityMarkdown);
        else if (File.Exists(identityPath))
            File.Delete(identityPath);

        if (dependencies is { Count: > 0 })
        {
            var sorted = dependencies.OrderBy(d => d.Target, StringComparer.Ordinal).ToList();
            var depsJson = JsonSerializer.Serialize(sorted, JsonOptions);
            File.WriteAllText(dependenciesPath, depsJson + "\n");
        }
        else if (File.Exists(dependenciesPath))
        {
            File.Delete(dependenciesPath);
        }
    }

    public void SaveFromSnapshot(string agenticOsPath, TopologyModelSnapshot snapshot)
    {
        foreach (var node in snapshot.Nodes)
        {
            var module = ToModuleFile(node);
            var deps = GetDependenciesFromSnapshot(snapshot, node.Id);
            SaveModule(agenticOsPath, module, node.Knowledge.Identity, deps);
        }
    }

    private static void ScanModulesRecursive(string dir, List<ModuleFile> results)
    {
        var moduleJsonPath = Path.Combine(dir, FileProtocolPaths.ModuleFileName);
        if (File.Exists(moduleJsonPath))
        {
            var json = File.ReadAllText(moduleJsonPath);
            var module = JsonSerializer.Deserialize<ModuleFile>(json, ReadOptions);
            if (module != null)
                results.Add(module);
        }

        foreach (var subDir in Directory.GetDirectories(dir))
            ScanModulesRecursive(subDir, results);
    }

    private static void SortArrays(ModuleFile module)
    {
        module.Keywords?.Sort(StringComparer.Ordinal);
        module.ExcludeDirs?.Sort(StringComparer.Ordinal);
        module.ManagedPaths?.Sort(StringComparer.Ordinal);
        module.CapabilityTags?.Sort(StringComparer.Ordinal);
        module.PublicApi?.Sort(StringComparer.Ordinal);
        module.Constraints?.Sort(StringComparer.Ordinal);
        module.Deliverables?.Sort(StringComparer.Ordinal);
        module.CollaborationIds?.Sort(StringComparer.Ordinal);
    }

    private static ProjectNodeRegistration ToProjectRegistration(ModuleFile module, string? identity)
        => new()
        {
            Id = module.Uid,
            Name = module.Name,
            Summary = ExtractSummary(identity),
            Vision = module.Vision,
            Steward = module.Steward,
            ExcludeDirs = NormalizePaths(module.ExcludeDirs),
            Metadata = CopyMetadata(module.Metadata),
            Knowledge = new TopologyKnowledgeSummaryModel { Identity = identity }
        };

    private static DepartmentNodeRegistration ToDepartmentRegistration(ModuleFile module, string? identity)
        => new()
        {
            Id = module.Uid,
            Name = module.Name,
            ParentId = module.Parent,
            Summary = ExtractSummary(identity),
            DisciplineCode = module.DisciplineCode ?? string.Empty,
            Scope = module.Scope,
            RoleId = string.IsNullOrWhiteSpace(module.RoleId) ? "coder" : module.RoleId.Trim(),
            Layers = module.Layers ?? [],
            Metadata = CopyMetadata(module.Metadata),
            Knowledge = new TopologyKnowledgeSummaryModel { Identity = identity }
        };

    private static TechnicalNodeRegistration ToTechnicalRegistration(ModuleFile module, string? identity, List<DependencyEntry> dependencies)
        => new()
        {
            Id = module.Uid,
            Name = module.Name,
            ParentId = module.Parent,
            Summary = ExtractSummary(identity),
            Maintainer = module.Maintainer,
            Layer = Math.Max(module.Layer ?? 0, 0),
            IsCrossWorkModule = module.IsCrossWorkModule == true,
            Participants = ToParticipants(module.Participants),
            Metadata = CopyMetadata(module.Metadata),
            PathBinding = new TopologyModulePathBindingModel
            {
                MainPath = NormalizePath(module.MainPath),
                ManagedPaths = NormalizePaths(module.ManagedPaths)
            },
            DeclaredDependencies = dependencies.Select(item => item.Target).ToList(),
            CapabilityTags = module.CapabilityTags ?? [],
            Contract = MergeContract(module, ExtractContract(identity)),
            Knowledge = new TopologyKnowledgeSummaryModel { Identity = identity }
        };

    private static TeamNodeRegistration ToTeamRegistration(ModuleFile module, string? identity, List<DependencyEntry> dependencies)
        => new()
        {
            Id = module.Uid,
            Name = module.Name,
            ParentId = module.Parent,
            Summary = ExtractSummary(identity),
            Maintainer = module.Maintainer,
            Layer = Math.Max(module.Layer ?? 0, 0),
            IsCrossWorkModule = module.IsCrossWorkModule == true,
            Participants = ToParticipants(module.Participants),
            Metadata = CopyMetadata(module.Metadata),
            PathBinding = new TopologyModulePathBindingModel
            {
                MainPath = NormalizePath(module.MainPath),
                ManagedPaths = NormalizePaths(module.ManagedPaths)
            },
            TechnicalDependencies = dependencies.Select(item => item.Target).ToList(),
            BusinessObjective = module.BusinessObjective,
            Deliverables = module.Deliverables ?? [],
            CollaborationIds = module.CollaborationIds ?? [],
            Knowledge = new TopologyKnowledgeSummaryModel { Identity = identity }
        };

    private static ModuleFile ToModuleFile(TopologyNode node)
    {
        var file = new ModuleFile
        {
            Uid = node.Id,
            Name = node.Name,
            Type = node.Kind,
            Parent = node.ParentId
        };

        switch (node)
        {
            case ProjectNode project:
                file.Vision = project.Vision;
                file.Steward = project.Steward;
                file.ExcludeDirs = project.ExcludeDirs.Count > 0 ? project.ExcludeDirs : null;
                file.Metadata = project.Metadata.Count > 0 ? project.Metadata : null;
                break;
            case DepartmentNode department:
                file.DisciplineCode = department.DisciplineCode;
                file.Scope = department.Scope;
                file.RoleId = department.RoleId;
                file.Layers = department.Layers.Count > 0 ? department.Layers : null;
                file.Metadata = department.Metadata.Count > 0 ? department.Metadata : null;
                break;
            case TechnicalNode technical:
                file.Maintainer = technical.Maintainer;
                file.MainPath = NormalizePath(technical.PathBinding.MainPath);
                file.ManagedPaths = technical.PathBinding.ManagedPaths.Count > 0 ? technical.PathBinding.ManagedPaths : null;
                file.Layer = technical.Layer;
                file.IsCrossWorkModule = technical.IsCrossWorkModule ? true : null;
                file.Participants = ToParticipantFiles(technical.Participants);
                file.Metadata = technical.Metadata.Count > 0 ? technical.Metadata : null;
                file.CapabilityTags = technical.CapabilityTags.Count > 0 ? technical.CapabilityTags : null;
                file.Boundary = technical.Contract.Boundary;
                file.PublicApi = technical.Contract.PublicApi.Count > 0 ? technical.Contract.PublicApi : null;
                file.Constraints = technical.Contract.Constraints.Count > 0 ? technical.Contract.Constraints : null;
                break;
            case TeamNode team:
                file.Maintainer = team.Maintainer;
                file.MainPath = NormalizePath(team.PathBinding.MainPath);
                file.ManagedPaths = team.PathBinding.ManagedPaths.Count > 0 ? team.PathBinding.ManagedPaths : null;
                file.Layer = team.Layer;
                file.IsCrossWorkModule = team.IsCrossWorkModule ? true : null;
                file.Participants = ToParticipantFiles(team.Participants);
                file.Metadata = team.Metadata.Count > 0 ? team.Metadata : null;
                file.BusinessObjective = team.BusinessObjective;
                file.Deliverables = team.Deliverables.Count > 0 ? team.Deliverables : null;
                file.CollaborationIds = team.CollaborationIds.Count > 0 ? team.CollaborationIds : null;
                break;
        }

        return file;
    }

    private static List<DependencyEntry> GetDependenciesFromSnapshot(TopologyModelSnapshot snapshot, string nodeId)
    {
        return snapshot.Dependencies
            .Where(relation => relation.FromId == nodeId)
            .Select(relation => new DependencyEntry
            {
                Target = relation.ToId,
                Type = "Association"
            })
            .OrderBy(item => item.Target, StringComparer.Ordinal)
            .ToList();
    }

    private static string? ExtractSummary(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return null;

        var lines = identity.Split('\n');
        var inSummary = false;
        var summaryLines = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## Summary", StringComparison.OrdinalIgnoreCase))
            {
                inSummary = true;
                continue;
            }

            if (inSummary && line.StartsWith("## ", StringComparison.Ordinal))
                break;

            if (inSummary)
                summaryLines.Add(line);
        }

        var result = string.Join('\n', summaryLines).Trim();
        return result.Length > 0 ? result : null;
    }

    private static TopologyModuleContractModel ExtractContract(string? identity)
    {
        if (string.IsNullOrWhiteSpace(identity))
            return new TopologyModuleContractModel();

        var lines = identity.Split('\n');
        var inContract = false;
        var inConstraints = false;
        var contractLines = new List<string>();
        var constraintLines = new List<string>();
        string? boundary = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("## Contract", StringComparison.OrdinalIgnoreCase))
            {
                inContract = true;
                inConstraints = false;
                continue;
            }

            if (line.StartsWith("## Constraints", StringComparison.OrdinalIgnoreCase))
            {
                inContract = false;
                inConstraints = true;
                continue;
            }

            if ((inContract || inConstraints) && line.StartsWith("## ", StringComparison.Ordinal))
            {
                inContract = false;
                inConstraints = false;
                continue;
            }

            if (inContract)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Boundary:", StringComparison.OrdinalIgnoreCase))
                    boundary = trimmed["Boundary:".Length..].Trim();
                else
                    contractLines.Add(line);
            }
            else if (inConstraints)
            {
                constraintLines.Add(line);
            }
        }

        return new TopologyModuleContractModel
        {
            Boundary = boundary,
            PublicApi = ParseBulletLines(contractLines),
            Constraints = ParseBulletLines(constraintLines)
        };
    }

    private static TopologyModuleContractModel MergeContract(ModuleFile module, TopologyModuleContractModel fallback)
    {
        return new TopologyModuleContractModel
        {
            Boundary = string.IsNullOrWhiteSpace(module.Boundary) ? fallback.Boundary : module.Boundary,
            PublicApi = module.PublicApi is { Count: > 0 } ? module.PublicApi : fallback.PublicApi,
            Constraints = module.Constraints is { Count: > 0 } ? module.Constraints : fallback.Constraints
        };
    }

    private static List<string> ParseBulletLines(IEnumerable<string> lines)
    {
        return lines
            .Where(line => line.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            .Select(line => line.TrimStart()[2..].Trim())
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> CopyMetadata(Dictionary<string, string>? metadata)
    {
        return metadata is { Count: > 0 }
            ? new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<TopologyCrossWorkParticipantDefinition> ToParticipants(List<CrossWorkParticipantFile>? participants)
    {
        if (participants is not { Count: > 0 })
            return [];

        return participants
            .Where(item => !string.IsNullOrWhiteSpace(item.ModuleName))
            .Select(item => new TopologyCrossWorkParticipantDefinition
            {
                ModuleName = item.ModuleName,
                Role = item.Role,
                ContractType = item.ContractType,
                Contract = item.Contract,
                Deliverable = item.Deliverable
            })
            .ToList();
    }

    private static List<CrossWorkParticipantFile>? ToParticipantFiles(List<TopologyCrossWorkParticipantDefinition> participants)
    {
        if (participants.Count == 0)
            return null;

        return participants
            .Where(item => !string.IsNullOrWhiteSpace(item.ModuleName))
            .Select(item => new CrossWorkParticipantFile
            {
                ModuleName = item.ModuleName,
                Role = item.Role,
                ContractType = item.ContractType,
                Contract = item.Contract,
                Deliverable = item.Deliverable
            })
            .ToList();
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalized = path.Replace('\\', '/').Trim().Trim('/');
        return normalized.Length == 0 ? null : normalized;
    }

    private static List<string> NormalizePaths(List<string>? paths)
    {
        if (paths is not { Count: > 0 })
            return [];

        var normalized = new List<string>();
        foreach (var path in paths)
        {
            var value = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(value))
                continue;

            if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
                normalized.Add(value);
        }

        return normalized;
    }
}
