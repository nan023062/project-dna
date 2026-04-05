using System.Text.Json.Serialization;
using Dna.Knowledge.TopoGraph.Models.Nodes;

namespace Dna.Knowledge.FileProtocol.Models;

public sealed class ModuleFile
{
    public string Uid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TopologyNodeKind Type { get; set; }

    public string? Parent { get; set; }
    public List<string>? Keywords { get; set; }
    public string? Maintainer { get; set; }
    public string? MainPath { get; set; }
    public List<string>? ManagedPaths { get; set; }
    public int? Layer { get; set; }
    public bool? IsCrossWorkModule { get; set; }
    public List<CrossWorkParticipantFile>? Participants { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    public string? Vision { get; set; }
    public string? Steward { get; set; }
    public List<string>? ExcludeDirs { get; set; }

    public string? DisciplineCode { get; set; }
    public string? Scope { get; set; }
    public string? RoleId { get; set; }
    public List<LayerDefinition>? Layers { get; set; }

    public List<string>? CapabilityTags { get; set; }
    public string? Boundary { get; set; }
    public List<string>? PublicApi { get; set; }
    public List<string>? Constraints { get; set; }

    public string? BusinessObjective { get; set; }
    public List<string>? Deliverables { get; set; }
    public List<string>? CollaborationIds { get; set; }
}
