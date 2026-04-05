namespace Dna.Knowledge;

public sealed class TopologyManagementSnapshot
{
    public List<string> ExcludeDirs { get; init; } = [];
    public List<TopologyDisciplineDefinition> Disciplines { get; init; } = [];
    public List<TopologyModuleDefinition> Modules { get; init; } = [];
    public List<TopologyCrossWorkDefinition> CrossWorks { get; init; } = [];
}

public sealed class TopologyDisciplineDefinition
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string RoleId { get; init; } = "coder";
    public List<LayerDefinition> Layers { get; init; } = [];
}

public sealed class TopologyModuleDefinition
{
    public string Discipline { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int Layer { get; set; }
    public string? ParentModuleId { get; set; }
    public List<string>? ManagedPaths { get; set; }
    public bool IsCrossWorkModule { get; set; }
    public List<TopologyCrossWorkParticipantDefinition> Participants { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
    public string? Maintainer { get; set; }
    public string? Summary { get; set; }
    public string? Boundary { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class TopologyCrossWorkDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Feature { get; set; }
    public List<TopologyCrossWorkParticipantDefinition> Participants { get; set; } = [];
}

public sealed class TopologyCrossWorkParticipantDefinition
{
    public string ModuleName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ContractType { get; set; }
    public string? Contract { get; set; }
    public string? Deliverable { get; set; }
}
