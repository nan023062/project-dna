using System.ComponentModel;
using System.Reflection;
using Dna.App.Services;
using Dna.App.Interfaces.Mcp;
using Dna.App.Services.Tooling;
using ModelContextProtocol.Server;

namespace Dna.App.Interfaces.Api;

public static class AppToolingEndpoints
{
    private static readonly IReadOnlyList<AppMcpToolDescriptor> McpToolCatalog = BuildMcpToolCatalog();

    public static void MapAppToolingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/app/tooling/list", (HttpRequest request, string? workspaceRoot, AppIdeToolingService tooling, AppWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var resolvedWorkspaceRoot = ResolveWorkspaceRoot(workspaces, workspaceRoot);
            var mcpEndpoint = ResolveMcpEndpoint(request);
            const string serverName = "project-dna";

            var targets = new[]
            {
                tooling.GetStatus("cursor", resolvedWorkspaceRoot, mcpEndpoint, serverName),
                tooling.GetStatus("codex", resolvedWorkspaceRoot, mcpEndpoint, serverName)
            };

            return Results.Ok(new
            {
                currentWorkspace,
                workspaceRoot = resolvedWorkspaceRoot,
                mcpEndpoint,
                serverName,
                targets
            });
        });

        app.MapPost("/api/app/tooling/install", (AppToolingInstallRequest request, HttpRequest httpRequest, AppIdeToolingService tooling, AppWorkspaceStore workspaces) =>
        {
            var currentWorkspace = workspaces.GetCurrentWorkspace();
            var workspaceRoot = ResolveWorkspaceRoot(workspaces, request.WorkspaceRoot);
            if (!Directory.Exists(workspaceRoot))
            {
                return Results.BadRequest(new
                {
                    error = $"workspaceRoot does not exist: {workspaceRoot}"
                });
            }

            var mcpEndpoint = ResolveMcpEndpoint(httpRequest);
            var serverName = string.IsNullOrWhiteSpace(request.ServerName)
                ? "project-dna"
                : request.ServerName.Trim();
            var replaceExisting = request.ReplaceExisting ?? true;

            var target = string.IsNullOrWhiteSpace(request.Target)
                ? "all"
                : request.Target.Trim().ToLowerInvariant();
            if (target is not ("all" or "cursor" or "codex"))
            {
                return Results.BadRequest(new
                {
                    error = "target must be one of: all, cursor, codex."
                });
            }

            var targetIds = target == "all"
                ? new[] { "cursor", "codex" }
                : new[] { target };

            var reports = targetIds
                .Select(id => tooling.InstallTarget(id, workspaceRoot, mcpEndpoint, serverName, replaceExisting))
                .ToList();
            var targets = targetIds
                .Select(id => tooling.GetStatus(id, workspaceRoot, mcpEndpoint, serverName))
                .ToList();

            return Results.Ok(new
            {
                currentWorkspace,
                workspaceRoot,
                mcpEndpoint,
                serverName,
                replaceExisting,
                reports,
                targets
            });
        });

        app.MapPost("/api/app/tooling/select-folder", async (
            AppToolingFolderPickRequest request,
            AppFolderPickerService folderPicker,
            AppWorkspaceStore workspaces,
            CancellationToken cancellationToken) =>
        {
            var defaultWorkspaceRoot = ResolveWorkspaceRoot(workspaces, request.DefaultWorkspaceRoot);
            var selected = await folderPicker.PickFolderAsync(defaultWorkspaceRoot, request.Prompt, cancellationToken);
            return Results.Ok(new
            {
                selected = !string.IsNullOrWhiteSpace(selected),
                workspaceRoot = string.IsNullOrWhiteSpace(selected) ? null : selected
            });
        });

        app.MapGet("/api/app/mcp/tools", (HttpRequest request) =>
        {
            var mcpEndpoint = ResolveMcpEndpoint(request);
            var groups = McpToolCatalog
                .GroupBy(item => item.Group)
                .Select(group => new
                {
                    group = group.Key,
                    count = group.Count()
                })
                .OrderBy(item => item.group, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Results.Ok(new
            {
                mcpEndpoint,
                total = McpToolCatalog.Count,
                groups,
                tools = McpToolCatalog
            });
        });
    }

    private static string ResolveMcpEndpoint(HttpRequest request)
        => $"{request.Scheme}://{request.Host}/mcp";

    private static string ResolveWorkspaceRoot(AppWorkspaceStore workspaces, string? requestWorkspaceRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(requestWorkspaceRoot))
            return Path.GetFullPath(requestWorkspaceRoot);

        return workspaces.GetCurrentWorkspace().WorkspaceRoot;
    }

    private static IReadOnlyList<AppMcpToolDescriptor> BuildMcpToolCatalog()
    {
        var toolTypes = new[] { typeof(KnowledgeTools), typeof(MemoryTools) };
        var descriptors = new List<AppMcpToolDescriptor>();

        foreach (var toolType in toolTypes)
        {
            var group = toolType.Name.Replace("Tools", string.Empty, StringComparison.Ordinal);
            var methods = toolType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
                    continue;

                var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description?.Trim()
                    ?? "无描述";
                var parameters = method.GetParameters()
                    .Select(parameter => new AppMcpToolParameterDescriptor
                    {
                        Name = parameter.Name ?? "unknown",
                        Type = FormatTypeName(parameter.ParameterType),
                        Required = !parameter.IsOptional,
                        DefaultValue = GetDefaultValue(parameter),
                        Description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description
                    })
                    .ToList();

                descriptors.Add(new AppMcpToolDescriptor
                {
                    Name = method.Name,
                    Group = group,
                    Description = description,
                    Parameters = parameters
                });
            }
        }

        return descriptors
            .OrderBy(item => item.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.IsOptional)
            return null;

        if (parameter.DefaultValue is null || parameter.DefaultValue is DBNull)
            return "null";

        return parameter.DefaultValue.ToString();
    }

    private static string FormatTypeName(Type type)
    {
        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return $"{FormatTypeName(nullable)}?";

        if (type.IsArray)
            return $"{FormatTypeName(type.GetElementType()!)}[]";

        if (type.IsGenericType)
        {
            var genericDef = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments().Select(FormatTypeName);
            if (genericDef == typeof(List<>))
                return $"List<{string.Join(", ", args)}>";
            if (genericDef == typeof(IReadOnlyList<>))
                return $"IReadOnlyList<{string.Join(", ", args)}>";
            if (genericDef == typeof(Task<>))
                return $"Task<{string.Join(", ", args)}>";
        }

        return type.Name switch
        {
            nameof(String) => "string",
            nameof(Boolean) => "bool",
            nameof(Int32) => "int",
            nameof(Int64) => "long",
            nameof(Double) => "double",
            nameof(Single) => "float",
            _ => type.Name
        };
    }
}

public sealed class AppToolingInstallRequest
{
    public string? Target { get; init; }
    public bool? ReplaceExisting { get; init; }
    public string? ServerName { get; init; }
    public string? WorkspaceRoot { get; init; }
}

public sealed class AppToolingFolderPickRequest
{
    public string? DefaultWorkspaceRoot { get; init; }
    public string? Prompt { get; init; }
}

public sealed class AppMcpToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<AppMcpToolParameterDescriptor> Parameters { get; init; } = [];
}

public sealed class AppMcpToolParameterDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Required { get; init; }
    public string? DefaultValue { get; init; }
    public string? Description { get; init; }
}
