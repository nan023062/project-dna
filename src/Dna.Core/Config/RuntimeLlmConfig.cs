using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dna.Core.Config;

public sealed class RuntimeLlmConfigDocument
{
    public List<RuntimeLlmProviderConfig> Providers { get; set; } = [];
    public string? ActiveProviderId { get; set; }
    public RuntimeLlmPurposeBindings Purposes { get; set; } = new();
}

public sealed class RuntimeLlmProviderConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ProviderType { get; set; } = "openai";
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string EmbeddingBaseUrl { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public string ApiKeySource { get; set; } = "env";
    public string? ApiKeyEnvVar { get; set; }
    public string? ApiKey { get; set; }
    public bool Enabled { get; set; } = true;
    public List<string> Tags { get; set; } = [];
}

public sealed class RuntimeLlmPurposeBindings
{
    public string? Chat { get; set; }
    public string? Embedding { get; set; }
    public string? Review { get; set; }
}

public static class RuntimeLlmConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static RuntimeLlmConfigDocument LoadOrCreate(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        if (!File.Exists(normalizedPath))
        {
            var empty = CreateDefault();
            Save(normalizedPath, empty);
            return empty;
        }

        try
        {
            var json = File.ReadAllText(normalizedPath);
            return Normalize(JsonSerializer.Deserialize<RuntimeLlmConfigDocument>(json, JsonOptions));
        }
        catch
        {
            var fallback = CreateDefault();
            Save(normalizedPath, fallback);
            return fallback;
        }
    }

    public static RuntimeLlmConfigDocument Save(string filePath, RuntimeLlmConfigDocument? document)
    {
        var normalizedPath = NormalizePath(filePath);
        var normalizedDocument = Normalize(document);
        Directory.CreateDirectory(Path.GetDirectoryName(normalizedPath)!);
        File.WriteAllText(normalizedPath, JsonSerializer.Serialize(normalizedDocument, JsonOptions));
        return normalizedDocument;
    }

    public static RuntimeLlmConfigDocument CreateDefault()
        => new()
        {
            Providers = [],
            ActiveProviderId = null,
            Purposes = new RuntimeLlmPurposeBindings()
        };

    public static RuntimeLlmConfigDocument Normalize(RuntimeLlmConfigDocument? document)
    {
        var normalized = document ?? CreateDefault();
        normalized.Providers ??= [];
        normalized.Purposes ??= new RuntimeLlmPurposeBindings();

        normalized.Providers = normalized.Providers
            .Select(NormalizeProvider)
            .Where(provider => !string.IsNullOrWhiteSpace(provider.Id))
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        normalized.ActiveProviderId = NormalizeProviderReference(normalized.ActiveProviderId, normalized.Providers);
        normalized.Purposes.Chat = NormalizeProviderReference(normalized.Purposes.Chat, normalized.Providers);
        normalized.Purposes.Embedding = NormalizeProviderReference(normalized.Purposes.Embedding, normalized.Providers);
        normalized.Purposes.Review = NormalizeProviderReference(normalized.Purposes.Review, normalized.Providers);

        return normalized;
    }

    private static RuntimeLlmProviderConfig NormalizeProvider(RuntimeLlmProviderConfig? provider)
    {
        provider ??= new RuntimeLlmProviderConfig();
        provider.Id = (provider.Id ?? string.Empty).Trim();
        provider.Name = (provider.Name ?? string.Empty).Trim();
        provider.ProviderType = NormalizeLower(provider.ProviderType, "openai");
        provider.BaseUrl = (provider.BaseUrl ?? string.Empty).Trim();
        provider.Model = (provider.Model ?? string.Empty).Trim();
        provider.EmbeddingBaseUrl = (provider.EmbeddingBaseUrl ?? string.Empty).Trim();
        provider.EmbeddingModel = (provider.EmbeddingModel ?? string.Empty).Trim();
        provider.ApiKeySource = NormalizeLower(provider.ApiKeySource, string.IsNullOrWhiteSpace(provider.ApiKey) ? "env" : "plain");
        provider.ApiKeyEnvVar = NormalizeOptional(provider.ApiKeyEnvVar);
        provider.ApiKey = NormalizeOptional(provider.ApiKey);
        provider.Tags = (provider.Tags ?? [])
            .Select(tag => (tag ?? string.Empty).Trim())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return provider;
    }

    private static string NormalizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("filePath is required.", nameof(filePath));

        return Path.GetFullPath(filePath);
    }

    private static string NormalizeLower(string? value, string fallback)
    {
        var normalized = NormalizeOptional(value);
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized.ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeProviderReference(string? providerId, IReadOnlyCollection<RuntimeLlmProviderConfig> providers)
    {
        var normalized = NormalizeOptional(providerId);
        if (normalized is null)
            return null;

        return providers.Any(provider => string.Equals(provider.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ? normalized
            : null;
    }
}
