using Dna.Core.Framework;

namespace Dna.Client.Interfaces.Cli;

public sealed class DefaultCliCommand : ICliCommand
{
    public string Name => "default";
    public string Description => "客户端命令（当前仅输出运行说明）";

    public Task<int> ExecuteAsync(string[] args)
    {
        Console.WriteLine("Project DNA Client");
        Console.WriteLine("运行方式：");
        Console.WriteLine("  Client --stdio --server http://localhost:5051");
        Console.WriteLine("  Client --port 5052 --server http://localhost:5051");
        return Task.FromResult(0);
    }
}
