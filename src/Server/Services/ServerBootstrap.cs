using System.Net;
using System.Net.Sockets;

namespace Dna.Services;

public static class ServerBootstrap
{
    public static ServerRuntimeOptions CreateRuntimeOptions(string[] args)
    {
        var dataPath = ResolveDataPath(args);
        Environment.SetEnvironmentVariable("DNA_STORE_PATH", dataPath);

        return new ServerRuntimeOptions
        {
            DataPath = dataPath
        };
    }

    public static string GetLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 80);
            if (socket.LocalEndPoint is IPEndPoint ep)
                return ep.Address.ToString();
        }
        catch
        {
        }

        return "localhost";
    }

    private static string ResolveDataPath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!string.Equals(args[i], "--db", StringComparison.OrdinalIgnoreCase))
                continue;

            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                return Path.GetFullPath(args[i + 1]);

            return Directory.GetCurrentDirectory();
        }

        var envStore = Environment.GetEnvironmentVariable("DNA_STORE_PATH");
        if (!string.IsNullOrEmpty(envStore))
            return Path.GetFullPath(envStore);

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("错误：必须指定知识库路径。");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("用法：");
        Console.WriteLine("  dna --db                          # 用当前目录作为知识库");
        Console.WriteLine("  dna --db <知识库目录>");
        Console.WriteLine("  dna --db --port 5051              # 当前目录 + 指定端口");
        Console.WriteLine("  dna --db ~/.dna/my-game --port 5051");
        Console.WriteLine();
        Console.WriteLine("或设置环境变量 DNA_STORE_PATH。");
        Environment.Exit(1);
        return "";
    }
}
