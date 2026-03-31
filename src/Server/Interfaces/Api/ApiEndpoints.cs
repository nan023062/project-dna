namespace Dna.Interfaces.Api;

/// <summary>
/// 统一 REST API 路由注册入口
/// 各领域 Endpoint 分文件实现，此处仅负责组装
/// </summary>
public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app, DateTime startedAt)
    {
        app.MapStatusEndpoints(startedAt);
        app.MapAgentEndpoints();
        app.MapTopologyEndpoints();
        app.MapGraphEndpoints();
        app.MapGovernanceEndpoints();
        app.MapConfigEndpoints();
        app.MapMemoryEndpoints();
        app.MapReviewEndpoints();
        app.MapModuleManagementEndpoints();
        app.MapFileTreeEndpoints();
    }
}
