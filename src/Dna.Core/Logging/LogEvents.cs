using Microsoft.Extensions.Logging;

namespace Dna.Core.Logging;

/// <summary>
/// 统一的领域事件 ID，自定义 ILoggerProvider 据此分配 emoji / 颜色 / tag。
/// 使用方式：logger.LogInformation(LogEvents.Mcp, "get_topology() ...");
/// </summary>
public static class LogEvents
{
    public static readonly EventId Mcp       = new(1000, "MCP");
    public static readonly EventId Api       = new(1100, "API");
    public static readonly EventId Topo      = new(2000, "TOPO");
    public static readonly EventId Module    = new(2100, "MODULE");
    public static readonly EventId Dna       = new(2200, "DNA");
    public static readonly EventId Stack     = new(2300, "STACK");
    public static readonly EventId Evolve    = new(3000, "EVOLVE");
    public static readonly EventId Feedback  = new(3100, "FEEDBACK");
    public static readonly EventId Workspace = new(4000, "WORKSPACE");
}
