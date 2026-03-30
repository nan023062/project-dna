using System.Text.Json;
using Dna.Client.Interfaces.Cli;
using Dna.Client.Services;
using Dna.Client.Services.Pipeline;
using Dna.Core.Framework;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var serverBaseUrl = ResolveServerBaseUrl(args);

DnaApp.Create(args, new AppOptions
{
    AppName = "Project DNA Client",
    AppDescription = "决策与执行客户端（MCP + Agent 入口）",
    DefaultPort = 5052,
    BannerExtras = (_, port) =>
    {
        var host = GetLocalIp();
        return
        [
            ("Client API:  ", $"http://{host}:{port}/api/client/status"),
            ("MCP Server:  ", $"http://{host}:{port}/mcp"),
            ("DNA Server:  ", serverBaseUrl)
        ];
    }
});

DnaApp.AddCliCommand(new DefaultCliCommand());

DnaApp.ConfigureServices(services =>
{
    services.AddSingleton(new ClientRuntimeOptions { ServerBaseUrl = serverBaseUrl });
    services.AddHttpClient<DnaServerApi>((_, client) =>
    {
        client.BaseAddress = new Uri(serverBaseUrl.TrimEnd('/'));
        client.Timeout = TimeSpan.FromSeconds(30);
    });
    services.AddSingleton<ClientPipelineStore>();
    services.AddSingleton<AgentPipelineRunner>();

    if (DnaApp.Mode == AppRunMode.Stdio)
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithStdioServerTransport().WithToolsFromAssembly();
    }
    else
    {
        services.AddMcpServer(opts =>
        {
            opts.ServerInfo = new() { Name = "project-dna-client", Version = "1.0.0" };
        }).WithHttpTransport().WithToolsFromAssembly();
    }
});

DnaApp.ConfigureWebApp(web =>
{
    web.MapGet("/api/client/status", async (DnaServerApi api) =>
    {
        try
        {
            var serverStatus = await api.GetAsync("/api/status");
            return Results.Ok(new
            {
                client = "ok",
                targetServer = api.BaseUrl,
                serverStatus
            });
        }
        catch (Exception ex)
        {
            return Results.Ok(new
            {
                client = "degraded",
                targetServer = api.BaseUrl,
                error = ex.Message
            });
        }
    });

    web.MapGet("/api/status", async (DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync("/api/status"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/topology", async (DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync("/api/topology"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/memory/stats", async (DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync("/api/memory/stats"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/memory/query", async (
        string? nodeTypes,
        string? layers,
        string? disciplines,
        string? features,
        string? types,
        string? tags,
        string? nodeId,
        string? freshness,
        int? limit,
        int? offset,
        DnaServerApi api) =>
    {
        try
        {
            var path = BuildApiPath("/api/memory/query", new Dictionary<string, string?>
            {
                ["nodeTypes"] = nodeTypes,
                ["layers"] = layers,
                ["disciplines"] = disciplines,
                ["features"] = features,
                ["types"] = types,
                ["tags"] = tags,
                ["nodeId"] = nodeId,
                ["freshness"] = freshness,
                ["limit"] = limit?.ToString(),
                ["offset"] = offset?.ToString()
            });
            return Results.Json(await api.GetAsync(path));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/memory/{id}", async (string id, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync($"/api/memory/{Uri.EscapeDataString(id)}"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapPost("/api/memory/remember", async (JsonElement request, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.PostAsync("/api/memory/remember", request));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapPut("/api/memory/{id}", async (string id, JsonElement request, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.PutAsync($"/api/memory/{Uri.EscapeDataString(id)}", request));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapDelete("/api/memory/{id}", async (string id, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.DeleteAsync($"/api/memory/{Uri.EscapeDataString(id)}"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/review/memory/submissions/mine", async (DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync("/api/review/memory/submissions/mine"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/review/memory/submissions/{id}", async (string id, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.GetAsync($"/api/review/memory/submissions/{Uri.EscapeDataString(id)}"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapPost("/api/review/memory/submissions", async (JsonElement request, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.PostAsync("/api/review/memory/submissions", request));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapPut("/api/review/memory/submissions/{id}", async (string id, JsonElement request, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.PutAsync($"/api/review/memory/submissions/{Uri.EscapeDataString(id)}", request));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapDelete("/api/review/memory/submissions/{id}", async (string id, DnaServerApi api) =>
    {
        try
        {
            return Results.Json(await api.DeleteAsync($"/api/review/memory/submissions/{Uri.EscapeDataString(id)}"));
        }
        catch (Exception ex)
        {
            return HandleProxyError(ex, api.BaseUrl);
        }
    });

    web.MapGet("/api/client/pipeline/config", (ClientPipelineStore store) =>
    {
        var config = store.GetConfig();
        return Results.Ok(config);
    });

    web.MapPut("/api/client/pipeline/config", (
        PipelineUpdateRequest request,
        ClientPipelineStore store) =>
    {
        if (request.Config == null)
            return Results.BadRequest(new { error = "config 不能为空" });

        var updated = store.UpdateConfig(request.Config);
        return Results.Ok(updated);
    });

    web.MapGet("/api/client/pipeline/runs/latest", (ClientPipelineStore store) =>
    {
        var latest = store.GetLatestRun();
        return latest == null
            ? Results.NotFound(new { error = "暂无执行记录" })
            : Results.Ok(latest);
    });

    web.MapPost("/api/client/pipeline/run", async (
        PipelineRunRequest request,
        AgentPipelineRunner runner) =>
    {
        var result = await runner.RunAsync(request);
        return Results.Ok(result);
    });

    web.MapMcp("/mcp");
});

return await DnaApp.RunAsync();

static string BuildApiPath(string path, IReadOnlyDictionary<string, string?> query)
{
    var pairs = query
        .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
        .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}")
        .ToList();

    if (pairs.Count == 0) return path;
    return $"{path}?{string.Join("&", pairs)}";
}

static IResult HandleProxyError(Exception ex, string targetServer)
{
    var message = ex.Message ?? "Proxy request failed.";
    const string prefix = "HTTP ";
    if (message.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        var separator = message.IndexOf(':');
        if (separator > prefix.Length &&
            int.TryParse(message[prefix.Length..separator], out var statusCode))
        {
            var body = message[(separator + 1)..].Trim();
            return Results.Json(new { error = body, targetServer }, statusCode: statusCode);
        }
    }

    return Results.Json(new { error = message, targetServer }, statusCode: 502);
}

static string ResolveServerBaseUrl(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!string.Equals(args[i], "--server", StringComparison.OrdinalIgnoreCase)) continue;
        if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            return NormalizeUrl(args[i + 1]);
    }

    var env = Environment.GetEnvironmentVariable("DNA_SERVER_URL")
              ?? Environment.GetEnvironmentVariable("DNA_URL");
    if (!string.IsNullOrWhiteSpace(env))
        return NormalizeUrl(env);

    return "http://localhost:5051";
}

static string NormalizeUrl(string raw) => raw.Trim().TrimEnd('/');

static string GetLocalIp()
{
    try
    {
        using var socket = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 80);
        if (socket.LocalEndPoint is System.Net.IPEndPoint endpoint)
            return endpoint.Address.ToString();
    }
    catch
    {
    }

    return "localhost";
}
