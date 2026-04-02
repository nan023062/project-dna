using System.Collections.Concurrent;
using System.Text.Json;

namespace Dna.App.Services;

public sealed class ServerDiscoveryService(HttpClient httpClient, AppWorkspaceStore workspaceStore)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(20);
    private static readonly object CacheGate = new();
    private static DateTime _cachedAtUtc = DateTime.MinValue;
    private static List<DiscoveredServerInfo> _cachedServers = [];

    public async Task<List<DiscoveredServerInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        lock (CacheGate)
        {
            if ((DateTime.UtcNow - _cachedAtUtc) <= CacheTtl && _cachedServers.Count > 0)
                return _cachedServers.Select(Clone).ToList();
        }

        var candidates = BuildKnownCandidates();
        var found = new ConcurrentBag<DiscoveredServerInfo>();
        using var gate = new SemaphoreSlim(12);

        var tasks = candidates.Select(async baseUrl =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var probe = await TryProbeAsync(baseUrl, cancellationToken);
                if (probe is not null)
                    found.Add(probe);
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);

        var ordered = found
            .Select(Clone)
            .OrderByDescending(item => IsLocalHost(item.BaseUrl))
            .ThenByDescending(item => item.Allowed)
            .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (CacheGate)
        {
            _cachedServers = ordered.Select(Clone).ToList();
            _cachedAtUtc = DateTime.UtcNow;
        }

        return ordered;
    }

    public async Task<DiscoveredServerInfo> ProbeServerAsync(string serverBaseUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
            throw new ArgumentException("serverBaseUrl is required.");

        var normalized = NormalizeServerBaseUrl(serverBaseUrl);
        var probe = await TryProbeAsync(normalized, cancellationToken);
        return probe ?? throw new InvalidOperationException($"无法连接服务器：{normalized}");
    }

    public static void InvalidateCache()
    {
        lock (CacheGate)
        {
            _cachedServers = [];
            _cachedAtUtc = DateTime.MinValue;
        }
    }

    private async Task<DiscoveredServerInfo?> TryProbeAsync(string baseUrl, CancellationToken cancellationToken)
    {
        JsonElement status;
        try
        {
            status = await GetJsonAsync($"{baseUrl}/api/status", cancellationToken);
        }
        catch
        {
            return null;
        }

        var info = new DiscoveredServerInfo
        {
            BaseUrl = baseUrl,
            DisplayName = BuildDefaultDisplayName(baseUrl),
            ModuleCount = TryGetInt(status, "moduleCount"),
            StartedAt = TryGetString(status, "startedAt"),
            Uptime = TryGetString(status, "uptime")
        };

        try
        {
            var access = await GetJsonAsync($"{baseUrl}/api/connection/access", cancellationToken);
            info.Allowed = TryGetBool(access, "allowed");
            info.AccessReason = TryGetString(access, "reason");
        }
        catch
        {
            // 兼容未实现 access 接口的旧版本：默认当成可连接
            info.Allowed = true;
        }

        return info;
    }

    private async Task<JsonElement> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(1200));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");

        await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeoutCts.Token);
        return document.RootElement.Clone();
    }

    private List<string> BuildKnownCandidates()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snapshot = workspaceStore.GetSnapshot();

        foreach (var workspace in snapshot.Workspaces)
        {
            try
            {
                set.Add(NormalizeServerBaseUrl(workspace.ServerBaseUrl));
            }
            catch
            {
                // 忽略无效地址，避免坏配置影响其余连接。
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.CurrentWorkspace?.ServerBaseUrl))
        {
            try
            {
                set.Add(NormalizeServerBaseUrl(snapshot.CurrentWorkspace.ServerBaseUrl));
            }
            catch
            {
                // ignore
            }
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Defaults?.ServerBaseUrl))
        {
            try
            {
                set.Add(NormalizeServerBaseUrl(snapshot.Defaults.ServerBaseUrl));
            }
            catch
            {
                // ignore
            }
        }

        return set.ToList();
    }

    private static bool IsLocalHost(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string BuildDefaultDisplayName(string baseUrl)
    {
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return uri.Host;
        return baseUrl;
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : 0;
    }

    private static bool TryGetBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False) &&
               value.GetBoolean();
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static DiscoveredServerInfo Clone(DiscoveredServerInfo source)
    {
        return new DiscoveredServerInfo
        {
            BaseUrl = source.BaseUrl,
            DisplayName = source.DisplayName,
            Allowed = source.Allowed,
            AccessReason = source.AccessReason,
            ModuleCount = source.ModuleCount,
            StartedAt = source.StartedAt,
            Uptime = source.Uptime
        };
    }

    private static string NormalizeServerBaseUrl(string raw)
    {
        var normalized = AppBootstrap.NormalizeUrl(raw);
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            throw new ArgumentException($"serverBaseUrl is not a valid absolute URL: {raw}");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("serverBaseUrl must use http or https.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}

public sealed class DiscoveredServerInfo
{
    public string BaseUrl { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Allowed { get; set; }
    public string? AccessReason { get; set; }
    public int ModuleCount { get; set; }
    public string? StartedAt { get; set; }
    public string? Uptime { get; set; }
}
