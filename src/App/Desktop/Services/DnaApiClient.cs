using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Dna.App.Desktop.Services;

public class DnaApiClient : IDnaApiClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public DnaApiClient()
    {
        _http = new HttpClient();
        _baseUrl = "http://127.0.0.1:5052";
    }

    private string BuildUrl(string path)
    {
        if (path.StartsWith("http")) return path;
        return _baseUrl + (path.StartsWith("/") ? path : "/" + path);
    }

    public async Task<JsonElement> GetAsync(string path)
    {
        var response = await _http.GetAsync(BuildUrl(path));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<T> GetAsync<T>(string path)
    {
        var response = await _http.GetAsync(BuildUrl(path));
        response.EnsureSuccessStatusCode();
        return await DeserializeAsync<T>(response);
    }

    public async Task<JsonElement> PostAsync(string path, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(BuildUrl(path), content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<T> PostAsync<T>(string path, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(BuildUrl(path), content);
        response.EnsureSuccessStatusCode();
        return await DeserializeAsync<T>(response);
    }

    public async Task<JsonElement> PutAsync(string path, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(BuildUrl(path), content);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<T> PutAsync<T>(string path, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await _http.PutAsync(BuildUrl(path), content);
        response.EnsureSuccessStatusCode();
        return await DeserializeAsync<T>(response);
    }

    public async Task<JsonElement> DeleteAsync(string path)
    {
        var response = await _http.DeleteAsync(BuildUrl(path));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<T> DeleteAsync<T>(string path)
    {
        var response = await _http.DeleteAsync(BuildUrl(path));
        response.EnsureSuccessStatusCode();
        return await DeserializeAsync<T>(response);
    }

    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        if (request.RequestUri != null && !request.RequestUri.IsAbsoluteUri)
        {
            request.RequestUri = new Uri(BuildUrl(request.RequestUri.ToString()));
        }
        return _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<T>();
        if (payload is null)
            throw new InvalidOperationException($"Failed to deserialize response into {typeof(T).Name}.");

        return payload;
    }
}
