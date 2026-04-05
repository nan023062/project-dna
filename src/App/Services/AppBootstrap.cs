namespace Dna.App.Services;

public static class AppBootstrap
{
    public static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');
}
