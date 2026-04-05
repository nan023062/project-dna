using Dna.Knowledge.Workspace;

namespace Dna.Knowledge.Workspace.Models;

public sealed class WorkspaceDirectoryMetadataDocument
{
    public string Schema { get; set; } = WorkspaceConstants.Metadata.Schema;
    public string StableGuid { get; set; } = Guid.NewGuid().ToString("N");
    public string? Summary { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
