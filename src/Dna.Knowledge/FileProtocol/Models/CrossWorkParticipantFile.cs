namespace Dna.Knowledge.FileProtocol.Models;

public sealed class CrossWorkParticipantFile
{
    public string ModuleName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? ContractType { get; set; }
    public string? Contract { get; set; }
    public string? Deliverable { get; set; }
}
