using System.Text.Json.Serialization;
using Dna.Knowledge.TopoGraph.Models.Nodes;

namespace Dna.Knowledge.FileProtocol.Models;

/// <summary>
/// 对应 module.json 的文件模型。
/// </summary>
public sealed class ModuleFile
{
    /// <summary>模块唯一标识，必须与目录路径一致</summary>
    public string Uid { get; set; } = string.Empty;

    /// <summary>模块显示名</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>模块类型：Project / Department / Technical / Team</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TopologyNodeKind Type { get; set; }

    /// <summary>父模块 UID，根模块为 null</summary>
    public string? Parent { get; set; }

    /// <summary>定位关键词</summary>
    public List<string>? Keywords { get; set; }

    /// <summary>维护者</summary>
    public string? Maintainer { get; set; }

    /// <summary>关联的物理目录 StableGuid 列表</summary>
    public List<string>? ManagedPaths { get; set; }

    // --- Project 专属 ---

    /// <summary>项目愿景（Project 类型）</summary>
    public string? Vision { get; set; }

    /// <summary>项目负责人（Project 类型）</summary>
    public string? Steward { get; set; }

    // --- Department 专属 ---

    /// <summary>领域编码（Department 类型）</summary>
    public string? DisciplineCode { get; set; }

    /// <summary>作用域描述（Department 类型）</summary>
    public string? Scope { get; set; }

    // --- Technical 专属 ---

    /// <summary>能力标签（Technical 类型）</summary>
    public List<string>? CapabilityTags { get; set; }

    // --- Team 专属 ---

    /// <summary>业务目标（Team 类型）</summary>
    public string? BusinessObjective { get; set; }

    /// <summary>交付物列表（Team 类型）</summary>
    public List<string>? Deliverables { get; set; }
}
