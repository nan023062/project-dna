namespace Dna.Client.Services;

public static class ClientBootstrap
{
    public static string[] SanitizeArgsForFixedPort(string[]? args)
    {
        if (args is null || args.Length == 0)
            return [];

        var sanitized = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[i], "-p", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                    i++;
                continue;
            }

            sanitized.Add(args[i]);
        }

        return sanitized.ToArray();
    }

    public static int ResolveClientDefaultPort(string[]? args = null)
    {
        _ = args;
        return 5052;
    }

    public static string ResolveServerBaseUrl(string[] args)
    {
        if (TryResolveOption(args, "--server", out var cliValue))
            return NormalizeUrl(cliValue);

        var env = Environment.GetEnvironmentVariable("DNA_SERVER_URL")
                  ?? Environment.GetEnvironmentVariable("DNA_URL");
        if (!string.IsNullOrWhiteSpace(env))
            return NormalizeUrl(env);

        return "http://localhost:5051";
    }

    public static string GetLocalIp()
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 80);
            if (socket.LocalEndPoint is System.Net.IPEndPoint endpoint)
                return endpoint.Address.ToString();
        }
        catch
        {
        }

        return "localhost";
    }

    public static string ResolveWorkspaceRoot(string[]? args = null, string? requested = null)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(requested);

        if (TryResolveOption(args, "--workspace-root", out var cliWorkspaceRoot))
            return Path.GetFullPath(cliWorkspaceRoot);

        var envWorkspace = Environment.GetEnvironmentVariable("DNA_WORKSPACE_ROOT");
        if (!string.IsNullOrWhiteSpace(envWorkspace))
            return Path.GetFullPath(envWorkspace);

        var cwd = Directory.GetCurrentDirectory();
        if (!LooksLikeClientProjectDirectory(cwd))
            return cwd;

        var repoRoot = Path.GetFullPath(Path.Combine(cwd, "..", ".."));
        return Directory.Exists(repoRoot) ? repoRoot : cwd;
    }

    public static string? ResolveWorkspaceConfigPath(string[]? args = null)
    {
        if (TryResolveOption(args, "--workspace-config", out var cliPath))
            return Path.GetFullPath(cliPath);

        var envPath = Environment.GetEnvironmentVariable("DNA_CLIENT_WORKSPACE_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
            return Path.GetFullPath(envPath);

        return null;
    }

    public static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');

    private static bool TryResolveOption(string[]? args, string optionName, out string value)
    {
        value = string.Empty;
        if (args is null || args.Length == 0)
            return false;

        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                value = args[i + 1];
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeClientProjectDirectory(string path)
    {
        try
        {
            if (!File.Exists(Path.Combine(path, "Client.csproj")))
                return false;
            if (!Directory.Exists(Path.Combine(path, "wwwroot")))
                return false;

            var repoRoot = Path.GetFullPath(Path.Combine(path, "..", ".."));
            return Directory.Exists(Path.Combine(repoRoot, "src")) &&
                   Directory.Exists(Path.Combine(repoRoot, "client-tools"));
        }
        catch
        {
            return false;
        }
    }
}
