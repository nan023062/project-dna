using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dna.Core.Config;
using Microsoft.Extensions.Logging;

namespace Dna.Memory.Services;

/// <summary>
/// 向量生成服务 — 调用 OpenAI 兼容的 /embeddings 端点生成语义向量。
///
/// 兼容性设计：
/// - 必须在 Provider 中显式配置 EmbeddingModel 才会调用，不假设任何默认模型
/// - EmbeddingBaseUrl 未配时复用 Provider.BaseUrl
/// - dimensions 参数默认不发送，完全信任模型返回的原生维度
/// - 未配置时静默返回 null，上层自动降级到 FTS 检索
/// </summary>
internal class EmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ProjectConfig _config;
    private readonly ILogger<EmbeddingService> _logger;
    private bool _loggedSkipOnce;

    /// <summary>是否已显式配置 Embedding 模型</summary>
    public bool IsAvailable
    {
        get
        {
            var provider = _config.GetActiveLlmProvider();
            return provider != null
                   && !string.IsNullOrEmpty(provider.ApiKey)
                   && !string.IsNullOrEmpty(provider.EmbeddingModel);
        }
    }

    public EmbeddingService(
        IHttpClientFactory httpClientFactory,
        ProjectConfig config,
        ILogger<EmbeddingService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 为单段文本生成嵌入向量。
    /// 未配置 EmbeddingModel 时直接返回 null（静默降级，不发 HTTP 请求）。
    /// </summary>
    public async Task<float[]?> GenerateEmbeddingAsync(string text)
    {
        var provider = _config.GetActiveLlmProvider();
        if (provider == null || string.IsNullOrEmpty(provider.ApiKey))
            return null;

        if (string.IsNullOrEmpty(provider.EmbeddingModel))
        {
            if (!_loggedSkipOnce)
            {
                _logger.LogInformation("未配置 Embedding 模型，语义检索将使用 FTS 降级模式。" +
                    "在 Dashboard → LLM 设置中填写 Embedding 模型即可启用向量检索。");
                _loggedSkipOnce = true;
            }
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("embedding");

            var baseUrl = !string.IsNullOrEmpty(provider.EmbeddingBaseUrl)
                ? provider.EmbeddingBaseUrl
                : provider.BaseUrl ?? "https://api.openai.com/v1";
            baseUrl = baseUrl.TrimEnd('/');

            var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/embeddings")
            {
                Content = JsonContent.Create(new EmbeddingRequest
                {
                    Input = text,
                    Model = provider.EmbeddingModel
                }, options: JsonOpts)
            };
            request.Headers.Add("Authorization", $"Bearer {provider.ApiKey}");

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(JsonOpts);
            var embedding = result?.Data?.FirstOrDefault()?.Embedding;

            if (embedding != null)
                _logger.LogDebug("Embedding 生成成功: {Model} {Dims} 维", provider.EmbeddingModel, embedding.Length);

            return embedding;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Embedding 生成失败（model={Model}, url={Url}），将降级到 FTS 检索",
                provider.EmbeddingModel,
                !string.IsNullOrEmpty(provider.EmbeddingBaseUrl) ? provider.EmbeddingBaseUrl : provider.BaseUrl);
            return null;
        }
    }

    /// <summary>批量生成嵌入向量</summary>
    public async Task<List<float[]?>> GenerateEmbeddingsBatchAsync(List<string> texts)
    {
        var results = new List<float[]?>();
        foreach (var text in texts)
        {
            results.Add(await GenerateEmbeddingAsync(text));
        }
        return results;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class EmbeddingRequest
    {
        [JsonPropertyName("input")]
        public string Input { get; init; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; init; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; init; }
    }
}
