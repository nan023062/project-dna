namespace Dna.ExternalAgent.Contracts;

public interface IExternalAgentFolderPicker
{
    Task<string?> PickFolderAsync(
        string? defaultPath = null,
        string? prompt = null,
        CancellationToken cancellationToken = default);
}
