using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Models;
using Dna.Knowledge.Project.Models;

namespace Dna.Interfaces.Api;

public static class ModuleManagementEndpoints
{
    public static void MapModuleManagementEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/modules");

        group.MapGet("/manifest", (IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var arch = graph.GetArchitecture();
            var manifest = graph.GetModulesManifest();
            var merged = arch.Disciplines.ToDictionary(
                kv => kv.Key,
                kv => new
                {
                    displayName = kv.Value.DisplayName ?? kv.Key,
                    layers = kv.Value.Layers,
                    modules = manifest.Disciplines.GetValueOrDefault(kv.Key, [])
                });
            return Results.Ok(new
            {
                disciplines = merged,
                crossWorks = manifest.CrossWorks,
                features = manifest.Features
            });
        });

        // ── Module CRUD ──

        group.MapPost("/", (UpsertModuleRequest request, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            if (string.IsNullOrWhiteSpace(request.Discipline))
                return Results.BadRequest(new { error = "discipline 不能为空" });

            if (request.Module is null)
                return Results.BadRequest(new { error = "module 不能为空" });

            if (string.IsNullOrWhiteSpace(request.Module.Name) || string.IsNullOrWhiteSpace(request.Module.Path))
                return Results.BadRequest(new { error = "module.name 与 module.path 不能为空" });

            var discipline = request.Discipline.Trim();
            var isCrossWork = request.Module.IsCrossWorkModule;

            if (isCrossWork)
            {
                var (computedDisc, computedLayer) = ComputeCwOwnership(
                    request.Module.Participants, graph.GetModulesManifest());
                discipline = computedDisc;
                request.Module.Layer = computedLayer;
            }
            else
            {
                // DB-first 设计下 discipline 可按需创建，不再依赖静态 architecture 配置。
                graph.UpsertDiscipline(discipline, discipline, "coder", []);
            }

            try
            {
                graph.RegisterModule(discipline, request.Module);
                graph.BuildTopology();
                return Results.Ok(new { message = "模块保存成功" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        group.MapDelete("/{moduleName}", (string moduleName, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var ok = graph.UnregisterModule(moduleName);
            if (!ok) return Results.NotFound(new { error = $"未找到模块: {moduleName}" });
            graph.BuildTopology();
            return Results.Ok(new { message = $"模块已删除: {moduleName}" });
        });

        // ── Discipline CRUD ──

        group.MapGet("/disciplines", (IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var arch = graph.GetArchitecture();
            var manifest = graph.GetModulesManifest();
            var result = arch.Disciplines.Select(kv => new
            {
                id = kv.Key,
                displayName = kv.Value.DisplayName ?? kv.Key,
                roleId = kv.Value.RoleId,
                layers = kv.Value.Layers,
                moduleCount = manifest.Disciplines.GetValueOrDefault(kv.Key, []).Count
            }).OrderBy(d => d.id);
            return Results.Ok(result);
        });

        group.MapPost("/disciplines", (UpsertDisciplineRequest request, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var id = request.Id?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(id))
                return Results.BadRequest(new { error = "discipline id 不能为空" });

            if (request.Layers is null || request.Layers.Count == 0)
                return Results.BadRequest(new { error = "至少需要定义一个 Layer" });

            var levels = request.Layers.Select(l => l.Level).ToList();
            if (levels.Distinct().Count() != levels.Count)
                return Results.BadRequest(new { error = "Layer level 不能重复" });

            graph.UpsertDiscipline(id, request.DisplayName, request.RoleId ?? "coder", request.Layers);
            return Results.Ok(new { message = $"部门 '{id}' 已保存", id });
        });

        group.MapDelete("/disciplines/{disciplineId}", (string disciplineId, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var manifest = graph.GetModulesManifest();
            var moduleCount = manifest.Disciplines.GetValueOrDefault(disciplineId, []).Count;
            if (moduleCount > 0)
                return Results.BadRequest(new { error = $"部门 '{disciplineId}' 下还有 {moduleCount} 个模块，请先删除模块" });

            var ok = graph.RemoveDiscipline(disciplineId);
            if (!ok) return Results.NotFound(new { error = $"未找到部门: {disciplineId}" });
            return Results.Ok(new { message = $"部门已删除: {disciplineId}" });
        });

        // ── CrossWork CRUD ──

        group.MapGet("/crossworks", (IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var manifest = graph.GetModulesManifest();
            return Results.Ok(manifest.CrossWorks);
        });

        group.MapPost("/crossworks", (CrossWorkRegistration request, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest(new { error = "crosswork.name 不能为空" });

            request.Participants ??= [];
            request.Participants = request.Participants
                .Where(p => !string.IsNullOrWhiteSpace(p.ModuleName))
                .ToList();

            graph.SaveCrossWork(request);
            graph.BuildTopology();
            return Results.Ok(new { message = "CrossWork 保存成功", id = request.Id });
        });

        group.MapDelete("/crossworks/{id}", (string id, IGraphEngine graph, ProjectConfig config) =>
        {
            EnsureReady(graph, config);
            var ok = graph.RemoveCrossWork(id);
            if (!ok) return Results.NotFound(new { error = $"未找到 CrossWork: {id}" });
            graph.BuildTopology();
            return Results.Ok(new { message = $"CrossWork 已删除: {id}" });
        });
    }

    private static (string discipline, int layer) ComputeCwOwnership(
        List<CrossWorkParticipantRegistration> participants, ModulesManifest manifest)
    {
        if (participants is not { Count: > 0 })
            return ("root", 0);

        var participantNames = new HashSet<string>(
            participants.Select(p => p.ModuleName), StringComparer.OrdinalIgnoreCase);

        var disciplines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxLayer = 0;

        foreach (var (discipline, modules) in manifest.Disciplines)
        {
            foreach (var m in modules)
            {
                if (!participantNames.Contains(m.Name)) continue;
                disciplines.Add(discipline);
                if (m.Layer > maxLayer) maxLayer = m.Layer;
            }
        }

        return disciplines.Count == 1
            ? (disciplines.First(), maxLayer)
            : ("root", 0);
    }

    private static void EnsureReady(IGraphEngine graph, ProjectConfig config)
    {
    }

    public sealed class UpsertModuleRequest
    {
        public string Discipline { get; set; } = string.Empty;
        public ModuleRegistration? Module { get; set; }
    }

    public sealed class UpsertDisciplineRequest
    {
        public string Id { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? RoleId { get; set; }
        public List<LayerDefinition> Layers { get; set; } = [];
    }
}
