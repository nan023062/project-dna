using System.Text.Json;
using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

public static class ClientProxyEndpoints
{
    public static void MapClientProxyEndpoints(this IEndpointRouteBuilder app)
    {
        // Core service status and topology read.
        MapProxyGet(app, "/api/status");
        MapProxyGet(app, "/api/topology");
        MapProxyGet(app, "/api/connection/access");

        // Formal memory read/write proxy.
        MapMemoryProxyEndpoints(app);
    }

    private static void MapMemoryProxyEndpoints(IEndpointRouteBuilder app)
    {
        MapProxyGet(app, "/api/memory/stats");
        MapProxyGetWithQuery(app, "/api/memory/query", "/api/memory/query",
            "nodeTypes", "layers", "disciplines", "features", "types", "tags", "nodeId", "freshness", "limit", "offset");

        MapProxyGetById(app, "/api/memory/{id}", id => $"/api/memory/{Uri.EscapeDataString(id)}");
        MapProxyPost(app, "/api/memory/remember");
        MapProxyPutById(app, "/api/memory/{id}", id => $"/api/memory/{Uri.EscapeDataString(id)}");
        MapProxyDeleteById(app, "/api/memory/{id}", id => $"/api/memory/{Uri.EscapeDataString(id)}");
    }

    private static void MapProxyGet(IEndpointRouteBuilder app, string localPath, string? remotePath = null)
    {
        var upstream = remotePath ?? localPath;
        app.MapGet(localPath, async (DnaServerApi api) =>
        {
            try
            {
                return Results.Json(await api.GetAsync(upstream));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static void MapProxyGetWithQuery(
        IEndpointRouteBuilder app,
        string localPath,
        string remotePath,
        params string[] allowedQueryKeys)
    {
        app.MapGet(localPath, async (HttpRequest request, DnaServerApi api) =>
        {
            try
            {
                var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in allowedQueryKeys)
                {
                    if (!request.Query.TryGetValue(key, out var value)) continue;
                    var text = value.ToString();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    query[key] = text;
                }

                var upstreamPath = BuildApiPath(remotePath, query);
                return Results.Json(await api.GetAsync(upstreamPath));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static void MapProxyPost(IEndpointRouteBuilder app, string localPath, string? remotePath = null)
    {
        var upstream = remotePath ?? localPath;
        app.MapPost(localPath, async (JsonElement request, DnaServerApi api) =>
        {
            try
            {
                return Results.Json(await api.PostAsync(upstream, request));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static void MapProxyGetById(IEndpointRouteBuilder app, string localPath, Func<string, string> remotePathFactory)
    {
        app.MapGet(localPath, async (string id, DnaServerApi api) =>
        {
            try
            {
                return Results.Json(await api.GetAsync(remotePathFactory(id)));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static void MapProxyPutById(IEndpointRouteBuilder app, string localPath, Func<string, string> remotePathFactory)
    {
        app.MapPut(localPath, async (string id, JsonElement request, DnaServerApi api) =>
        {
            try
            {
                return Results.Json(await api.PutAsync(remotePathFactory(id), request));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static void MapProxyDeleteById(IEndpointRouteBuilder app, string localPath, Func<string, string> remotePathFactory)
    {
        app.MapDelete(localPath, async (string id, DnaServerApi api) =>
        {
            try
            {
                return Results.Json(await api.DeleteAsync(remotePathFactory(id)));
            }
            catch (Exception ex)
            {
                return ClientProxyErrorResults.Create(ex, api.BaseUrl);
            }
        });
    }

    private static string BuildApiPath(string path, IReadOnlyDictionary<string, string?> query)
    {
        var pairs = query
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
            .ToList();

        if (pairs.Count == 0) return path;
        return $"{path}?{string.Join("&", pairs)}";
    }

}
