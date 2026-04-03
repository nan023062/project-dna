using Dna.Knowledge.FileProtocol.Models;
using Dna.Knowledge.TopoGraph.Contracts;
using Dna.Knowledge.TopoGraph.Models.Registrations;

namespace Dna.Knowledge.FileProtocol;

public sealed class FileBasedDefinitionStore : ITopoGraphDefinitionStore
{
    private readonly KnowledgeFileStore _fileStore = new();
    private string _agenticOsPath = string.Empty;
    private bool _initialized;

    public void Initialize(string storePath)
    {
        _agenticOsPath = ResolveAgenticOsPath(storePath);
        _initialized = true;
    }

    public void Reload()
    {
    }

    public TopologyModelDefinition LoadDefinition()
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return new TopologyModelDefinition();

        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);
        if (!Directory.Exists(modulesRoot))
            return new TopologyModelDefinition();

        return _fileStore.LoadAsDefinition(_agenticOsPath);
    }

    public void SaveDefinition(TopologyModelDefinition definition)
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return;

        SaveDefinitionToFiles(definition);
    }

    public bool HasKnowledgeFiles()
    {
        if (!_initialized || string.IsNullOrEmpty(_agenticOsPath))
            return false;

        var modulesRoot = FileProtocolPaths.GetModulesRoot(_agenticOsPath);
        return Directory.Exists(modulesRoot) &&
               Directory.GetFiles(modulesRoot, FileProtocolPaths.ModuleFileName, SearchOption.AllDirectories).Length > 0;
    }

    public string AgenticOsPath => _agenticOsPath;

    private void SaveDefinitionToFiles(TopologyModelDefinition definition)
    {
        var existingUids = _fileStore.LoadModules(_agenticOsPath)
            .Select(module => module.Uid)
            .ToHashSet(StringComparer.Ordinal);
        var desiredUids = new HashSet<string>(StringComparer.Ordinal);

        if (definition.Project != null)
        {
            desiredUids.Add(definition.Project.Id);
            SaveRegistrationAsModule(definition.Project, null);
        }

        foreach (var department in definition.Departments)
        {
            desiredUids.Add(department.Id);
            SaveRegistrationAsModule(department, null);
        }

        foreach (var technical in definition.TechnicalNodes)
        {
            desiredUids.Add(technical.Id);
            SaveRegistrationAsModule(technical, technical.DeclaredDependencies);
        }

        foreach (var team in definition.TeamNodes)
        {
            desiredUids.Add(team.Id);
            SaveRegistrationAsModule(team, team.TechnicalDependencies);
        }

        CleanupRemovedModules(existingUids, desiredUids);
    }

    private void SaveRegistrationAsModule(TopologyNodeRegistration registration, List<string>? dependencyTargets)
    {
        var module = RegistrationToModuleFile(registration);
        var identity = registration.Knowledge.Identity;
        var dependencies = dependencyTargets?
            .Select(target => new DependencyEntry { Target = target, Type = "Association" })
            .ToList();

        _fileStore.SaveModule(_agenticOsPath, module, identity, dependencies);
    }

    private static ModuleFile RegistrationToModuleFile(TopologyNodeRegistration registration)
    {
        var module = new ModuleFile
        {
            Uid = registration.Id,
            Name = registration.Name,
            Parent = registration.ParentId
        };

        switch (registration)
        {
            case ProjectNodeRegistration project:
                module.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Project;
                module.Vision = project.Vision;
                module.Steward = project.Steward;
                module.ExcludeDirs = project.ExcludeDirs.Count > 0 ? project.ExcludeDirs : null;
                module.Metadata = project.Metadata.Count > 0 ? project.Metadata : null;
                break;
            case DepartmentNodeRegistration department:
                module.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Department;
                module.DisciplineCode = department.DisciplineCode;
                module.Scope = department.Scope;
                module.RoleId = department.RoleId;
                module.Layers = department.Layers.Count > 0 ? department.Layers : null;
                module.Metadata = department.Metadata.Count > 0 ? department.Metadata : null;
                break;
            case TechnicalNodeRegistration technical:
                module.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Technical;
                module.Maintainer = technical.Maintainer;
                module.MainPath = technical.PathBinding.MainPath;
                module.ManagedPaths = technical.PathBinding.ManagedPaths.Count > 0 ? technical.PathBinding.ManagedPaths : null;
                module.Layer = technical.Layer;
                module.IsCrossWorkModule = technical.IsCrossWorkModule ? true : null;
                module.Participants = ToParticipantFiles(technical.Participants);
                module.Metadata = technical.Metadata.Count > 0 ? technical.Metadata : null;
                module.CapabilityTags = technical.CapabilityTags.Count > 0 ? technical.CapabilityTags : null;
                module.Boundary = technical.Contract.Boundary;
                module.PublicApi = technical.Contract.PublicApi.Count > 0 ? technical.Contract.PublicApi : null;
                module.Constraints = technical.Contract.Constraints.Count > 0 ? technical.Contract.Constraints : null;
                break;
            case TeamNodeRegistration team:
                module.Type = TopoGraph.Models.Nodes.TopologyNodeKind.Team;
                module.Maintainer = team.Maintainer;
                module.MainPath = team.PathBinding.MainPath;
                module.ManagedPaths = team.PathBinding.ManagedPaths.Count > 0 ? team.PathBinding.ManagedPaths : null;
                module.Layer = team.Layer;
                module.IsCrossWorkModule = team.IsCrossWorkModule ? true : null;
                module.Participants = ToParticipantFiles(team.Participants);
                module.Metadata = team.Metadata.Count > 0 ? team.Metadata : null;
                module.BusinessObjective = team.BusinessObjective;
                module.Deliverables = team.Deliverables.Count > 0 ? team.Deliverables : null;
                module.CollaborationIds = team.CollaborationIds.Count > 0 ? team.CollaborationIds : null;
                break;
        }

        return module;
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

    private static string ResolveAgenticOsPath(string storePath)
    {
        if (string.IsNullOrWhiteSpace(storePath))
            return string.Empty;

        var normalized = storePath.Replace('\\', '/').TrimEnd('/');
        if (normalized.EndsWith("/" + FileProtocolPaths.AgenticOsDir, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(normalized), FileProtocolPaths.AgenticOsDir, StringComparison.OrdinalIgnoreCase))
        {
            return storePath;
        }

        var agenticOsDir = Path.GetDirectoryName(storePath);
        if (agenticOsDir != null &&
            string.Equals(Path.GetFileName(agenticOsDir), FileProtocolPaths.AgenticOsDir, StringComparison.OrdinalIgnoreCase))
        {
            return agenticOsDir;
        }

        var candidateDir = Path.Combine(storePath, FileProtocolPaths.AgenticOsDir);
        if (Directory.Exists(candidateDir))
            return candidateDir;

        var current = storePath;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(current, FileProtocolPaths.AgenticOsDir);
            if (Directory.Exists(candidate))
                return candidate;

            var parent = Path.GetDirectoryName(current);
            if (parent == current)
                break;

            current = parent ?? string.Empty;
        }

        return storePath;
    }

    private void CleanupRemovedModules(HashSet<string> existingUids, HashSet<string> desiredUids)
    {
        foreach (var uid in existingUids.Where(uid => !desiredUids.Contains(uid)).OrderByDescending(uid => uid.Length))
        {
            var moduleDir = FileProtocolPaths.GetModuleDir(_agenticOsPath, uid);
            if (Directory.Exists(moduleDir))
                Directory.Delete(moduleDir, recursive: true);
        }
    }
}
