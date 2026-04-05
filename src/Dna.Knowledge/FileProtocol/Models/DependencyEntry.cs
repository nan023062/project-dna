namespace Dna.Knowledge.FileProtocol.Models;

/// <summary>
/// 对应 dependencies.json 中的单条依赖声明。
/// </summary>
public sealed class DependencyEntry
{
    /// <summary>目标模块 UID</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>依赖类型，默认 Association</summary>
    public string? Type { get; set; }

    /// <summary>依赖说明</summary>
    public string? Note { get; set; }
}
