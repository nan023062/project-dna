namespace Dna.Core.Framework;

/// <summary>
/// CLI 命令契约 — 每个命令是一个独立的功能模块。
/// 通过 DnaApp.AddCliCommand 注册，框架根据命令名自动路由。
/// </summary>
public interface ICliCommand
{
    /// <summary>命令名（匹配 args 中的子命令，如 "status"、"topology"）</summary>
    string Name { get; }

    /// <summary>命令说明（用于 help 输出）</summary>
    string Description { get; }

    /// <summary>执行命令，返回退出码</summary>
    Task<int> ExecuteAsync(string[] args);
}
