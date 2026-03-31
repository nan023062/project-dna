using System.Text.Json;
using Dna.Client.Services;

namespace Dna.Client.Interfaces.Api;

internal static class ClientProxyErrorResults
{
    public static IResult Create(Exception ex, string fallbackTargetServer)
    {
        if (ex is DnaServerApiException apiException)
            return CreateUpstreamError(apiException);

        var message = string.IsNullOrWhiteSpace(ex.Message)
            ? "Proxy request failed."
            : ex.Message.Trim();

        return Results.Json(
            new
            {
                error = message,
                targetServer = fallbackTargetServer
            },
            statusCode: StatusCodes.Status502BadGateway);
    }

    private static IResult CreateUpstreamError(DnaServerApiException ex)
    {
        var error = string.IsNullOrWhiteSpace(ex.ResponseBody)
            ? ex.ReasonPhrase ?? "Request failed."
            : ex.ResponseBody.Trim();

        if (TryParseJson(ex.ResponseBody, out var upstreamBody))
        {
            error = TryExtractError(upstreamBody, error);
            return Results.Json(
                new
                {
                    error,
                    targetServer = ex.TargetServer,
                    upstreamStatusCode = ex.StatusCode,
                    upstreamBody
                },
                statusCode: ex.StatusCode);
        }

        return Results.Json(
            new
            {
                error,
                targetServer = ex.TargetServer,
                upstreamStatusCode = ex.StatusCode,
                upstreamText = ex.ResponseBody
            },
            statusCode: ex.StatusCode);
    }

    private static bool TryParseJson(string payload, out JsonElement body)
    {
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                using var document = JsonDocument.Parse(payload);
                body = document.RootElement.Clone();
                return true;
            }
            catch (JsonException)
            {
            }
        }

        body = default;
        return false;
    }

    private static string TryExtractError(JsonElement body, string fallback)
    {
        if (body.ValueKind == JsonValueKind.Object)
        {
            if (body.TryGetProperty("error", out var error))
                return JsonElementToText(error, fallback);

            if (body.TryGetProperty("message", out var message))
                return JsonElementToText(message, fallback);

            if (body.TryGetProperty("title", out var title))
                return JsonElementToText(title, fallback);
        }

        return JsonElementToText(body, fallback);
    }

    private static string JsonElementToText(JsonElement value, string fallback)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Null => fallback,
            JsonValueKind.Undefined => fallback,
            _ => value.GetRawText()
        };
    }
}
