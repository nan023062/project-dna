namespace Dna.Core.Framework;

/// <summary>
/// 应用配置选项 — 由具体应用提供，框架不硬编码任何产品信息。
/// </summary>
public sealed class AppOptions
{
    /// <summary>应用名称（用于日志、横幅、锁前缀）</summary>
    public string AppName { get; init; } = "App";

    /// <summary>应用描述（横幅副标题）</summary>
    public string AppDescription { get; init; } = "";

    /// <summary>默认端口</summary>
    public int DefaultPort { get; init; } = 5000;

    /// <summary>是否自动打开浏览器</summary>
    public bool OpenBrowser { get; init; } = true;

    /// <summary>
    /// 单实例锁范围提供者 — 返回一个字符串标识当前运行范围。
    /// 相同范围不允许启动多个实例。默认使用当前工作目录。
    /// </summary>
    public Func<IServiceProvider, string>? LockScopeProvider { get; init; }

    /// <summary>
    /// 日志目录提供者 — 返回日志文件的存放目录。
    /// 留空则不启用文件日志。
    /// </summary>
    public Func<IServiceProvider, string?>? LogDirectoryProvider { get; init; }

    /// <summary>
    /// 启动后回调 — 在所有服务就绪后、开始监听前执行。
    /// 适合做项目初始化、数据加载等。
    /// </summary>
    public Func<IServiceProvider, Task>? OnStarted { get; init; }

    /// <summary>
    /// 自定义横幅行 — 在标准端口信息之后额外输出的行。
    /// </summary>
    public Func<IServiceProvider, int, IEnumerable<(string Label, string Value)>>? BannerExtras { get; init; }

}
