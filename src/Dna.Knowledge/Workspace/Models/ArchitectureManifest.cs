using Dna.Knowledge;

namespace Dna.Knowledge.Workspace.Models;

/// <summary>
/// Root structure of architecture.json.
/// Defines the discipline and layer layout that modules can register into.
/// </summary>
public sealed class ArchitectureManifest
{
    public Dictionary<string, DisciplineDefinition> Disciplines { get; set; } = new();

    /// <summary>
    /// Additional directory names to exclude during workspace scanning.
    /// They are merged into the default exclusion list.
    /// </summary>
    public List<string>? ExcludeDirs { get; set; }

    /// <summary>
    /// Optional persona configuration used by MCP and desktop presentation.
    /// </summary>
    public PersonaConfig? Persona { get; set; }
}

/// <summary>
/// Persona configuration stored in architecture.json.
/// </summary>
public sealed class PersonaConfig
{
    public string Name { get; set; } = WorkspaceConstants.Persona.DefaultName;

    public string? ShortName { get; set; }

    public string? Description { get; set; }

    public string? Greeting { get; set; }
}

/// <summary>
/// Discipline definition in the architecture layer.
/// </summary>
public sealed class DisciplineDefinition
{
    public string? DisplayName { get; set; }

    public string RoleId { get; set; } = WorkspaceConstants.Persona.DefaultRoleId;

    public List<LayerDefinition> Layers { get; set; } = [];
}

/// <summary>
/// Default directory names excluded from workspace scanning.
/// </summary>
public static class DefaultExcludes
{
    public static readonly HashSet<string> Dirs =
        new(WorkspaceConstants.ExcludedDirectories.Names, StringComparer.OrdinalIgnoreCase);

    public static HashSet<string> BuildWithCustom(IEnumerable<string>? customDirs)
    {
        var merged = new HashSet<string>(Dirs, StringComparer.OrdinalIgnoreCase);
        if (customDirs == null)
            return merged;

        foreach (var dir in customDirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;

            merged.Add(dir.Trim());
        }

        return merged;
    }
}
