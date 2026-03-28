namespace Dna.Core.Framework;

/// <summary>
/// 服务基础契约 — 所有注册到 DnaApp 的服务必须实现此接口。
///
/// 设计原则：
/// - 功能内聚：每个服务封装一类完整能力，对外只暴露服务 API
/// - 完全解耦：服务之间不直接引用，只有业务层组合不同服务完成业务逻辑
/// - 生命周期可控：DnaApp 统一管理初始化和销毁
/// </summary>
public interface IDnaService
{
    /// <summary>服务名称（用于日志和诊断）</summary>
    string ServiceName { get; }

    /// <summary>
    /// 初始化 — 在所有服务注册完成后，由 DnaApp 按注册顺序调用。
    /// </summary>
    Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// 关闭 — 在应用退出时，由 DnaApp 按反向顺序调用。
    /// </summary>
    Task ShutdownAsync() => Task.CompletedTask;
}
