using System.Text;
using System.Text.Json;

namespace Dna.App.Services;

public sealed class DnaServerApi(HttpClient httpClient, AppRuntimeOptions options)
{
    public string BaseUrl => options.ApiBaseUrl;

    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PostAsync(string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, path, body ?? new { });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PutAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Put, path, body);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Delete, path);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body = null,
        CancellationToken cancellationToken = default,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        var request = CreateRequest(method, path, body);
        return httpClient.SendAsync(request, completionOption, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, BuildUri(path));
        if (body is not null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private Uri BuildUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute) &&
            (string.Equals(absolute.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absolute;
        }

        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? options.ApiBaseUrl : BaseUrl;
        return new Uri(new Uri($"{baseUrl.TrimEnd('/')}/"), path.TrimStart('/'));
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new DnaServerApiException(
                (int)response.StatusCode,
                response.RequestMessage?.RequestUri?.GetLeftPart(UriPartial.Authority) ?? BaseUrl,
                payload,
                response.ReasonPhrase);
        }

        if (string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}").RootElement.Clone();

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
