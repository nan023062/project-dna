using System.Text;
using System.Text.Json;

namespace Dna.Client.Services;

public sealed class DnaServerApi(HttpClient httpClient, ClientRuntimeOptions options)
{
    public string BaseUrl => options.ServerBaseUrl;

    public async Task<JsonElement> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PostAsync(string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(
            body == null ? "{}" : JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PostAsync(path, content, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> PutAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.PutAsync(path, content, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public async Task<JsonElement> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.DeleteAsync(path, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var message = string.IsNullOrWhiteSpace(payload)
                ? response.ReasonPhrase ?? "请求失败"
                : payload;
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {message}");
        }

        if (string.IsNullOrWhiteSpace(payload))
            return JsonDocument.Parse("{}").RootElement.Clone();

        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
