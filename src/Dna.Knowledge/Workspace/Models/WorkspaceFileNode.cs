using Dna.Knowledge.Workspace;

namespace Dna.Knowledge.Workspace.Models;

/// <summary>
/// A structured workspace entry fact used by knowledge, memory and governance layers.
/// </summary>
public sealed class WorkspaceFileNode
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string ParentPath { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public WorkspaceEntryKind Kind { get; init; } = WorkspaceEntryKind.Directory;
    public FileNodeStatus Status { get; init; } = FileNodeStatus.Untracked;
    public string StatusLabel { get; init; } = WorkspaceConstants.Labels.Untracked;
    public string? Badge { get; init; }
    public string? Extension { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime? LastModifiedUtc { get; init; }
    public bool Exists { get; init; } = true;
    public bool HasChildren { get; init; }
    public int ChildDirectoryCount { get; init; }
    public int ChildFileCount { get; init; }
    public WorkspaceOwnershipInfo? Ownership { get; init; }
    public FileNodeModuleInfo? Module { get; init; }
    public WorkspaceDirectoryDescriptorInfo? Descriptor { get; init; }
    public FileNodeActions Actions { get; init; } = new();
    public List<WorkspaceFileNode>? Children { get; init; }

    public WorkspaceFileNode Clone()
    {
        return new WorkspaceFileNode
        {
            Name = Name,
            Path = Path,
            ParentPath = ParentPath,
            FullPath = FullPath,
            Kind = Kind,
            Status = Status,
            StatusLabel = StatusLabel,
            Badge = Badge,
            Extension = Extension,
            SizeBytes = SizeBytes,
            LastModifiedUtc = LastModifiedUtc,
            Exists = Exists,
            HasChildren = HasChildren,
            ChildDirectoryCount = ChildDirectoryCount,
            ChildFileCount = ChildFileCount,
            Ownership = Ownership?.Clone(),
            Module = Module?.Clone(),
            Descriptor = Descriptor?.Clone(),
            Actions = Actions.Clone(),
            Children = Children?.Select(static child => child.Clone()).ToList()
        };
    }
}

public enum WorkspaceEntryKind
{
    Directory,
    File
}

public enum FileNodeStatus
{
    Registered,
    CrossWork,
    Described,
    Managed,
    Tracked,
    Container,
    Candidate,
    Untracked
}

public sealed class FileNodeActions
{
    public bool CanRegister { get; init; }
    public bool CanEdit { get; init; }
    public string? SuggestedDiscipline { get; init; }
    public int? SuggestedLayer { get; init; }

    public FileNodeActions Clone()
    {
        return new FileNodeActions
        {
            CanRegister = CanRegister,
            CanEdit = CanEdit,
            SuggestedDiscipline = SuggestedDiscipline,
            SuggestedLayer = SuggestedLayer
        };
    }
}

public sealed class FileNodeModuleInfo
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Discipline { get; init; } = string.Empty;
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
    public string RegistrationPath { get; init; } = string.Empty;

    public FileNodeModuleInfo Clone()
    {
        return new FileNodeModuleInfo
        {
            Id = Id,
            Name = Name,
            Discipline = Discipline,
            Layer = Layer,
            IsCrossWorkModule = IsCrossWorkModule,
            RegistrationPath = RegistrationPath
        };
    }
}

public sealed class WorkspaceDirectoryDescriptorInfo
{
    public string FileName { get; init; } = WorkspaceConstants.Metadata.FileName;
    public string RelativeFilePath { get; init; } = string.Empty;
    public string StableGuid { get; init; } = string.Empty;
    public string? Summary { get; init; }

    public WorkspaceDirectoryDescriptorInfo Clone()
    {
        return new WorkspaceDirectoryDescriptorInfo
        {
            FileName = FileName,
            RelativeFilePath = RelativeFilePath,
            StableGuid = StableGuid,
            Summary = Summary
        };
    }
}

public sealed class WorkspaceOwnershipInfo
{
    public string ModuleId { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string Discipline { get; init; } = string.Empty;
    public int Layer { get; init; }
    public bool IsCrossWorkModule { get; init; }
    public WorkspaceOwnershipKind Kind { get; init; }
    public bool IsExactMatch { get; init; }
    public string ScopePath { get; init; } = string.Empty;

    public WorkspaceOwnershipInfo Clone()
    {
        return new WorkspaceOwnershipInfo
        {
            ModuleId = ModuleId,
            ModuleName = ModuleName,
            Discipline = Discipline,
            Layer = Layer,
            IsCrossWorkModule = IsCrossWorkModule,
            Kind = Kind,
            IsExactMatch = IsExactMatch,
            ScopePath = ScopePath
        };
    }
}

public enum WorkspaceOwnershipKind
{
    ModuleRoot,
    ManagedPath
}
