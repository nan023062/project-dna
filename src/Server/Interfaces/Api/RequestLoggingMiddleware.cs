using System.Diagnostics;
using Microsoft.Extensions.Logging;

using Dna.Core.Logging;

namespace Dna.Interfaces.Api;

/// <summary>
/// ASP.NET Core 中间件：记录每个 /api/ 请求的方法、路径、状态码、耗时。
/// 静态资源和 MCP 通道不记录，避免噪音。
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var method = context.Request.Method;
        var sw = Stopwatch.StartNew();

        try
        {
            await next(context);
        }
        finally
        {
            sw.Stop();
            var status = context.Response.StatusCode;
            logger.LogInformation(LogEvents.Api, "{Method} {Path} → {Status} ({Elapsed}ms)",
                method, path, status, sw.ElapsedMilliseconds);
        }
    }
}
