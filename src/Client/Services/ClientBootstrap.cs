namespace Dna.Client.Services;

public static class ClientBootstrap
{
    public static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');
}
