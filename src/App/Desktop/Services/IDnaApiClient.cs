using System.Text.Json;

namespace Dna.App.Desktop.Services;

public interface IDnaApiClient
{
    Task<JsonElement> GetAsync(string path);
    Task<JsonElement> PostAsync(string path, object payload);
    Task<JsonElement> PutAsync(string path, object payload);
    Task<JsonElement> DeleteAsync(string path);
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request);
}
