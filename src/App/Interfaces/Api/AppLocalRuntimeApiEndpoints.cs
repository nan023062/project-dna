using Dna.App.Services;
using Dna.Core.Config;
using Dna.Knowledge;
using Dna.Knowledge.Workspace;
using Dna.Knowledge.Workspace.Models;
using Dna.Memory.Models;
using Microsoft.AspNetCore.Mvc;

namespace Dna.App.Interfaces.Api;

public static class AppLocalRuntimeApiEndpoints
{
    public static void MapAppLocalRuntimeApiEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/status", (
            [FromServices] ITopoGraphApplicationService topology,
            [FromServices] IMemoryEngine memory,
            [FromServices] ProjectConfig config,
            [FromServices] AppProjectLlmConfigService llm,
            [FromServices] AppRuntimeOptions runtime) =>
        {
            var moduleCount = 0;
            try
            {
                moduleCount = topology.BuildTopology().Nodes.Count;
            }
            catch
            {
                // Ignore topology failures for lightweight status.
            }

            return Results.Json(new
            {
                serviceName = "Agentic OS Runtime",
                configured = config.HasProject,
                projectRoot = config.DefaultProjectRoot,
                storePath = config.MetadataRootPath,
                dataPath = config.MetadataRootPath,
                metadataRootPath = config.MetadataRootPath,
                memoryStorePath = config.MemoryStorePath,
                sessionStorePath = config.SessionStorePath,
                knowledgeStorePath = config.KnowledgeStorePath,
                projectName = runtime.ProjectName,
                moduleCount,
                memoryCount = memory.MemoryCount(),
                sessionCount = GetSessionEntries(memory, nodeId: null, limit: 5000).Count,
                startedAt = runtime.StartedAtUtc,
                uptime = (DateTime.UtcNow - runtime.StartedAtUtc).ToString(@"d\.hh\:mm\:ss"),
                transport = "Local REST + MCP",
                productMode = "single-user-local-app",
                runtimeLlm = llm.GetSummary()
            });
        });

        app.MapGet("/api/session", (
            [FromServices] IMemoryEngine memory,
            [FromQuery] string? nodeId,
            [FromQuery] int? limit) =>
        {
            var effectiveLimit = Math.Clamp(limit ?? 50, 1, 500);
            var items = GetSessionEntries(memory, nodeId, effectiveLimit)
                .OrderByDescending(entry => entry.CreatedAt)
                .Select(entry => new
                {
                    entry.Id,
                    type = entry.Type.ToString(),
                    stage = entry.Stage.ToString(),
                    category = InferSessionCategory(entry),
                    entry.NodeId,
                    entry.Summary,
                    entry.Content,
                    entry.Tags,
                    entry.CreatedAt
                })
                .ToList();

            return Results.Ok(new
            {
                count = items.Count,
                nodeId,
                items
            });
        });

        app.MapGet("/api/connection/access", (HttpContext context) =>
        {
            var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            return Results.Ok(new
            {
                allowed = true,
                role = "admin",
                entryName = "local-runtime",
                remoteIp,
                note = "single-process desktop runtime",
                reason = string.Empty
            });
        });

        app.MapGet("/api/workspace/tree", (
            [FromServices] IWorkspaceEngine workspace,
            [FromServices] ITopoGraphApplicationService topology,
            [FromServices] ProjectConfig config,
            [FromQuery] string? path,
            [FromQuery] int? maxDepth) =>
        {
            if (!config.HasProject)
                return Results.BadRequest(new { error = "Project is not configured." });

            var projectRoot = config.DefaultProjectRoot;
            var depth = Math.Clamp(maxDepth ?? 4, 0, 8);
            var tree = BuildWorkspaceDirectoryTree(
                workspace,
                projectRoot,
                topology.GetWorkspaceContext(),
                path,
                depth);

            return Results.Ok(tree);
        });
    }

    private static WorkspaceDirectoryTreeResponse BuildWorkspaceDirectoryTree(
        IWorkspaceEngine workspace,
        string projectRoot,
        WorkspaceTopologyContext topology,
        string? relativePath,
        int remainingDepth)
    {
        var snapshot = string.IsNullOrWhiteSpace(relativePath)
            ? workspace.GetRootSnapshot(projectRoot, topology)
            : workspace.GetDirectorySnapshot(projectRoot, relativePath, topology);

        return new WorkspaceDirectoryTreeResponse
        {
            ProjectRoot = snapshot.ProjectRoot,
            RelativePath = snapshot.RelativePath,
            Name = snapshot.Name,
            FullPath = snapshot.FullPath,
            Exists = snapshot.Exists,
            ScannedAtUtc = snapshot.ScannedAtUtc,
            DirectoryCount = snapshot.DirectoryCount,
            FileCount = snapshot.FileCount,
            Entries = snapshot.Entries
                .Select(entry => BuildWorkspaceEntryTree(
                    workspace,
                    projectRoot,
                    topology,
                    entry,
                    remainingDepth))
                .ToList()
        };
    }

    private static WorkspaceTreeEntryResponse BuildWorkspaceEntryTree(
        IWorkspaceEngine workspace,
        string projectRoot,
        WorkspaceTopologyContext topology,
        WorkspaceFileNode entry,
        int remainingDepth)
    {
        var response = new WorkspaceTreeEntryResponse
        {
            Name = entry.Name,
            Path = entry.Path,
            ParentPath = entry.ParentPath,
            FullPath = entry.FullPath,
            Kind = entry.Kind.ToString(),
            Status = entry.Status.ToString(),
            StatusLabel = entry.StatusLabel,
            Badge = entry.Badge,
            Extension = entry.Extension,
            SizeBytes = entry.SizeBytes,
            LastModifiedUtc = entry.LastModifiedUtc,
            Exists = entry.Exists,
            HasChildren = entry.HasChildren,
            ChildDirectoryCount = entry.ChildDirectoryCount,
            ChildFileCount = entry.ChildFileCount,
            Ownership = entry.Ownership is null
                ? null
                : new WorkspaceTreeOwnershipResponse
                {
                    ModuleId = entry.Ownership.ModuleId,
                    ModuleName = entry.Ownership.ModuleName,
                    Discipline = entry.Ownership.Discipline,
                    Layer = entry.Ownership.Layer,
                    IsCrossWorkModule = entry.Ownership.IsCrossWorkModule,
                    Kind = entry.Ownership.Kind.ToString(),
                    IsExactMatch = entry.Ownership.IsExactMatch,
                    ScopePath = entry.Ownership.ScopePath
                },
            Module = entry.Module is null
                ? null
                : new WorkspaceTreeModuleResponse
                {
                    Id = entry.Module.Id,
                    Name = entry.Module.Name,
                    Discipline = entry.Module.Discipline,
                    Layer = entry.Module.Layer,
                    IsCrossWorkModule = entry.Module.IsCrossWorkModule,
                    RegistrationPath = entry.Module.RegistrationPath
                },
            Descriptor = entry.Descriptor is null
                ? null
                : new WorkspaceTreeDescriptorResponse
                {
                    FileName = entry.Descriptor.FileName,
                    RelativeFilePath = entry.Descriptor.RelativeFilePath,
                    StableGuid = entry.Descriptor.StableGuid,
                    Summary = entry.Descriptor.Summary
                },
            Actions = new WorkspaceTreeActionsResponse
            {
                CanRegister = entry.Actions.CanRegister,
                CanEdit = entry.Actions.CanEdit,
                SuggestedDiscipline = entry.Actions.SuggestedDiscipline,
                SuggestedLayer = entry.Actions.SuggestedLayer
            }
        };

        if (entry.Kind == WorkspaceEntryKind.Directory && entry.HasChildren && remainingDepth > 0)
        {
            var snapshot = workspace.GetDirectorySnapshot(projectRoot, entry.Path, topology);
            response.Children = snapshot.Entries
                .Select(child => BuildWorkspaceEntryTree(
                    workspace,
                    projectRoot,
                    topology,
                    child,
                    remainingDepth - 1))
                .ToList();
        }

        return response;
    }

    private static List<MemoryEntry> GetSessionEntries(IMemoryEngine memory, string? nodeId, int limit)
    {
        return memory.QueryMemories(new MemoryFilter
        {
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId,
            Stages = [MemoryStage.ShortTerm],
            Freshness = FreshnessFilter.FreshAndAging,
            Limit = limit
        });
    }

    private static string InferSessionCategory(MemoryEntry entry)
    {
        if (entry.Type == MemoryType.Working ||
            entry.Tags.Contains(WellKnownTags.ActiveTask, StringComparer.OrdinalIgnoreCase))
        {
            return "tasks";
        }

        return "context";
    }

    private sealed class WorkspaceDirectoryTreeResponse
    {
        public string ProjectRoot { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public bool Exists { get; init; }
        public DateTime ScannedAtUtc { get; init; }
        public int DirectoryCount { get; init; }
        public int FileCount { get; init; }
        public List<WorkspaceTreeEntryResponse> Entries { get; init; } = [];
    }

    private sealed class WorkspaceTreeEntryResponse
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public string ParentPath { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string Kind { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StatusLabel { get; init; } = string.Empty;
        public string? Badge { get; init; }
        public string? Extension { get; init; }
        public long? SizeBytes { get; init; }
        public DateTime? LastModifiedUtc { get; init; }
        public bool Exists { get; init; }
        public bool HasChildren { get; init; }
        public int ChildDirectoryCount { get; init; }
        public int ChildFileCount { get; init; }
        public WorkspaceTreeOwnershipResponse? Ownership { get; init; }
        public WorkspaceTreeModuleResponse? Module { get; init; }
        public WorkspaceTreeDescriptorResponse? Descriptor { get; init; }
        public WorkspaceTreeActionsResponse Actions { get; init; } = new();
        public List<WorkspaceTreeEntryResponse>? Children { get; set; }
    }

    private sealed class WorkspaceTreeOwnershipResponse
    {
        public string ModuleId { get; init; } = string.Empty;
        public string ModuleName { get; init; } = string.Empty;
        public string Discipline { get; init; } = string.Empty;
        public int Layer { get; init; }
        public bool IsCrossWorkModule { get; init; }
        public string Kind { get; init; } = string.Empty;
        public bool IsExactMatch { get; init; }
        public string ScopePath { get; init; } = string.Empty;
    }

    private sealed class WorkspaceTreeModuleResponse
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Discipline { get; init; } = string.Empty;
        public int Layer { get; init; }
        public bool IsCrossWorkModule { get; init; }
        public string RegistrationPath { get; init; } = string.Empty;
    }

    private sealed class WorkspaceTreeDescriptorResponse
    {
        public string FileName { get; init; } = string.Empty;
        public string RelativeFilePath { get; init; } = string.Empty;
        public string StableGuid { get; init; } = string.Empty;
        public string? Summary { get; init; }
    }

    private sealed class WorkspaceTreeActionsResponse
    {
        public bool CanRegister { get; init; }
        public bool CanEdit { get; init; }
        public string? SuggestedDiscipline { get; init; }
        public int? SuggestedLayer { get; init; }
    }
}
