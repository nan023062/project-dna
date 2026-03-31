namespace Dna.Client.Services;

public static class ClientBootstrap
{
    public static string ResolveServerBaseUrl(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--server", StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                return NormalizeUrl(args[i + 1]);
        }

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

    private static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');
}
