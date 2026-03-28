using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Dna.Knowledge;

namespace Dna.Interfaces.Api;

public static class GovernanceEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/governance");

        api.MapGet("/validate", (
            IGraphEngine graph,
            IGovernanceEngine governance) =>
        {
            graph.BuildTopology();

            var report = governance.ValidateArchitecture();

            return Results.Json(new
            {
                healthy = report.IsHealthy,
                totalIssues = report.TotalIssues,
                cycleSuggestions = report.CycleSuggestions.Select(c => new
                {
                    c.Message,
                    c.Suggestion
                }),
                orphanNodes = report.OrphanNodes.Select(n => new
                {
                    n.Name,
                    n.Discipline,
                    type = n.Type.ToString()
                }),
                crossWorkIssues = report.CrossWorkIssues.Select(i => new
                {
                    i.CrossWorkName,
                    i.Message
                }),
                dependencyDrifts = report.DependencyDrifts.Select(d => new
                {
                    d.ModuleName,
                    d.Message,
                    d.Suggestion
                }),
                keyNodeWarnings = report.KeyNodeWarnings.Select(w => new
                {
                    w.NodeName,
                    w.Message
                })
            }, JsonOpts);
        });

        api.MapGet("/freshness", (
            IGovernanceEngine governance) =>
        {

            var decayed = governance.CheckFreshness();
            return Results.Json(new
            {
                decayedCount = decayed,
                message = decayed > 0
                    ? $"已降级 {decayed} 条过期记忆"
                    : "所有记忆鲜活度正常"
            }, JsonOpts);
        });

        api.MapPost("/condense/node", async (
            CondenseNodeRequest request,
            IGraphEngine graph,
            IGovernanceEngine governance) =>
        {
            graph.BuildTopology();

            if (string.IsNullOrWhiteSpace(request.NodeIdOrName))
                return Results.BadRequest(new { error = "nodeIdOrName 不能为空" });

            var result = await governance.CondenseNodeKnowledgeAsync(
                request.NodeIdOrName,
                request.MaxSourceMemories is > 0 ? request.MaxSourceMemories.Value : 200);

            return Results.Json(result, JsonOpts);
        });

        api.MapPost("/condense/all", async (
            CondenseAllRequest request,
            IGraphEngine graph,
            IGovernanceEngine governance) =>
        {
            graph.BuildTopology();

            var results = await governance.CondenseAllNodesAsync(
                request.MaxSourceMemories is > 0 ? request.MaxSourceMemories.Value : 200);

            return Results.Json(new
            {
                total = results.Count,
                condensed = results.Count(r => r.NewIdentityMemoryId != null),
                archived = results.Sum(r => r.ArchivedCount),
                results
            }, JsonOpts);
        });
    }

    public sealed class CondenseNodeRequest
    {
        public string NodeIdOrName { get; set; } = string.Empty;
        public int? MaxSourceMemories { get; set; }
    }

    public sealed class CondenseAllRequest
    {
        public int? MaxSourceMemories { get; set; }
    }
}
