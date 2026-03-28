using Dna.Core.Framework;

namespace Dna.Interfaces.Cli;

/// <summary>
/// 默认 CLI 命令 — 包装现有 CliHandler，支持所有子命令。
/// 后续可逐步将各子命令拆分为独立 ICliCommand 实现。
/// </summary>
public class DefaultCliCommand : ICliCommand
{
    public string Name => "default";
    public string Description => "CLI commands (status, topology, help)";

    public Task<int> ExecuteAsync(string[] args) => CliHandler.RunAsync(args);
}
