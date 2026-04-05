using System.Text.Json;

namespace Dna.App.Desktop.Services;

public interface IDnaApiClient
{
    Task<JsonElement> GetAsync(string path);
    Task<T> GetAsync<T>(string path);
    Task<JsonElement> PostAsync(string path, object payload);
    Task<T> PostAsync<T>(string path, object payload);
    Task<JsonElement> PutAsync(string path, object payload);
    Task<T> PutAsync<T>(string path, object payload);
    Task<JsonElement> DeleteAsync(string path);
    Task<T> DeleteAsync<T>(string path);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);
}
