namespace Dna.App.Services;

public sealed class DnaServerApiException : Exception
{
    public DnaServerApiException(
        int statusCode,
        string targetServer,
        string? responseBody,
        string? reasonPhrase = null,
        Exception? innerException = null)
        : base(BuildMessage(statusCode, targetServer, responseBody, reasonPhrase), innerException)
    {
        StatusCode = statusCode;
        TargetServer = targetServer;
        ResponseBody = responseBody ?? string.Empty;
        ReasonPhrase = reasonPhrase;
    }

    public int StatusCode { get; }

    public string TargetServer { get; }

    public string ResponseBody { get; }

    public string? ReasonPhrase { get; }

    private static string BuildMessage(int statusCode, string targetServer, string? responseBody, string? reasonPhrase)
    {
        var detail = string.IsNullOrWhiteSpace(responseBody)
            ? reasonPhrase ?? "Request failed."
            : responseBody.Trim();

        return $"Upstream server '{targetServer}' returned HTTP {statusCode}: {detail}";
    }
}
