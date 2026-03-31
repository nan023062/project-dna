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
        Console.WriteLine("  Client --server http://localhost:5051");
        Console.WriteLine("说明：Client 固定端口 5052，且同一台机器只允许运行一个实例。");
        return Task.FromResult(0);
    }
}
