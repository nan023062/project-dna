using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Dna.Auth;

namespace Dna.Services;

public sealed class ServerAllowlistStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private AllowlistState _state;

    public ServerAllowlistStore(ServerRuntimeOptions runtimeOptions)
    {
        Directory.CreateDirectory(runtimeOptions.DataPath);
        _filePath = Path.Combine(runtimeOptions.DataPath, "connection-allowlist.json");
        _state = LoadState();
        EnsureDefaults();
        SaveState();
    }

    public AllowlistSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new AllowlistSnapshot
            {
                Entries = _state.Entries
                    .Select(Clone)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Ip, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                UpdatedAtUtc = _state.UpdatedAtUtc
            };
        }
    }

    public AccessCheckResult Check(IPAddress? remoteIpAddress)
    {
        var normalized = NormalizeRemoteIp(remoteIpAddress);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new AccessCheckResult
            {
                Allowed = false,
                RemoteIp = "(unknown)",
                Reason = "无法识别来源 IP。"
            };
        }

        lock (_gate)
        {
            var hit = _state.Entries.FirstOrDefault(entry =>
                entry.Enabled &&
                string.Equals(entry.Ip, normalized, StringComparison.OrdinalIgnoreCase));

            if (hit is null)
            {
                return new AccessCheckResult
                {
                    Allowed = false,
                    RemoteIp = normalized,
                    Reason = "当前 IP 不在白名单。"
                };
            }

            return new AccessCheckResult
            {
                Allowed = true,
                RemoteIp = normalized,
                EntryId = hit.Id,
                EntryName = hit.Name,
                Role = hit.Role,
                Note = hit.Note
            };
        }
    }

    public AllowlistEntry Add(AllowlistMutationRequest request)
    {
        lock (_gate)
        {
            var ip = NormalizeIpOrThrow(request.Ip);
            var name = NormalizeNameOrThrow(request.Name);
            var note = NormalizeNote(request.Note);
            var enabled = request.Enabled ?? true;
            var role = NormalizeRoleOrDefault(request.Role, ServerRoles.Viewer);

            if (_state.Entries.Any(entry => string.Equals(entry.Ip, ip, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"IP 已存在：{ip}");

            var created = new AllowlistEntry
            {
                Id = $"ip-{Guid.NewGuid():N}"[..11],
                Ip = ip,
                Name = name,
                Note = note,
                Role = role,
                Enabled = enabled,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _state.Entries.Add(created);
            EnsureAtLeastOneAdminEntry();
            _state.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return Clone(created);
        }
    }

    public AllowlistEntry Update(string id, AllowlistMutationRequest request)
    {
        lock (_gate)
        {
            var existing = _state.Entries.FirstOrDefault(entry => entry.Id == id)
                ?? throw new KeyNotFoundException($"白名单条目不存在：{id}");

            if (!string.IsNullOrWhiteSpace(request.Ip))
            {
                var ip = NormalizeIpOrThrow(request.Ip);
                if (_state.Entries.Any(entry =>
                        entry.Id != id &&
                        string.Equals(entry.Ip, ip, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException($"IP 已存在：{ip}");
                }

                existing.Ip = ip;
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
                existing.Name = NormalizeNameOrThrow(request.Name);

            if (request.Note is not null)
                existing.Note = NormalizeNote(request.Note);

            if (request.Role is not null)
                existing.Role = NormalizeRoleOrDefault(request.Role, existing.Role);

            if (request.Enabled.HasValue)
                existing.Enabled = request.Enabled.Value;

            EnsureAtLeastOneAdminEntry();
            existing.UpdatedAtUtc = DateTime.UtcNow;
            _state.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return Clone(existing);
        }
    }

    public AllowlistEntry Delete(string id)
    {
        lock (_gate)
        {
            var index = _state.Entries.FindIndex(entry => entry.Id == id);
            if (index < 0)
                throw new KeyNotFoundException($"白名单条目不存在：{id}");

            var removed = Clone(_state.Entries[index]);
            _state.Entries.RemoveAt(index);

            EnsureLoopbackEntryExists();
            EnsureAtLeastOneAdminEntry();
            _state.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return removed;
        }
    }

    private AllowlistState LoadState()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AllowlistState();

            var json = File.ReadAllText(_filePath);
            var state = JsonSerializer.Deserialize<AllowlistState>(json, JsonOptions);
            return state ?? new AllowlistState();
        }
        catch
        {
            return new AllowlistState();
        }
    }

    private void SaveState()
    {
        var json = JsonSerializer.Serialize(_state, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private void EnsureDefaults()
    {
        EnsureLoopbackEntryExists();

        foreach (var ip in GetCurrentMachineIpv4Addresses())
        {
            if (_state.Entries.Any(entry => string.Equals(entry.Ip, ip, StringComparison.OrdinalIgnoreCase)))
                continue;

            _state.Entries.Add(new AllowlistEntry
            {
                Id = $"ip-{Guid.NewGuid():N}"[..11],
                Ip = ip,
                Name = $"本机网卡 {ip}",
                Note = "自动初始化",
                Role = ServerRoles.Admin,
                Enabled = true,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        EnsureAtLeastOneAdminEntry();
    }

    private void EnsureLoopbackEntryExists()
    {
        EnsureSystemEntry("127.0.0.1", "本机回环 IPv4");
        EnsureSystemEntry("::1", "本机回环 IPv6");
    }

    private void EnsureSystemEntry(string ip, string name)
    {
        var hit = _state.Entries.FirstOrDefault(entry =>
            string.Equals(entry.Ip, ip, StringComparison.OrdinalIgnoreCase));
        if (hit is not null)
        {
            hit.Enabled = true;
            hit.Name = string.IsNullOrWhiteSpace(hit.Name) ? name : hit.Name;
            hit.Role = NormalizeRoleOrDefault(hit.Role, ServerRoles.Admin);
            hit.UpdatedAtUtc = DateTime.UtcNow;
            return;
        }

        _state.Entries.Add(new AllowlistEntry
        {
            Id = $"ip-{Guid.NewGuid():N}"[..11],
            Ip = ip,
            Name = name,
            Note = "系统默认白名单",
            Role = ServerRoles.Admin,
            Enabled = true,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }

    private void EnsureAtLeastOneAdminEntry()
    {
        if (_state.Entries.Any(entry =>
                entry.Enabled &&
                string.Equals(entry.Role, ServerRoles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var fallback = _state.Entries.FirstOrDefault(entry =>
                           entry.Enabled &&
                           (string.Equals(entry.Ip, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(entry.Ip, "::1", StringComparison.OrdinalIgnoreCase)))
                       ?? _state.Entries.FirstOrDefault(entry => entry.Enabled)
                       ?? _state.Entries.FirstOrDefault();

        if (fallback is null)
            return;

        fallback.Role = ServerRoles.Admin;
        fallback.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static List<string> GetCurrentMachineIpv4Addresses()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            IPInterfaceProperties? properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var ip = NormalizeRemoteIp(address);
                if (string.IsNullOrWhiteSpace(ip))
                    continue;

                set.Add(ip);
            }
        }

        return set.ToList();
    }

    private static string NormalizeIpOrThrow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new ArgumentException("ip 不能为空。");

        if (!IPAddress.TryParse(raw.Trim(), out var address))
            throw new ArgumentException($"ip 非法：{raw}");

        return NormalizeRemoteIp(address) ?? throw new ArgumentException($"ip 非法：{raw}");
    }

    private static string NormalizeNameOrThrow(string? raw)
    {
        var name = raw?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name 不能为空。");
        return name;
    }

    private static string? NormalizeNote(string? note)
    {
        var normalized = note?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string NormalizeRoleOrDefault(string? raw, string defaultRole)
    {
        var role = string.IsNullOrWhiteSpace(raw) ? defaultRole : raw.Trim().ToLowerInvariant();
        return role switch
        {
            ServerRoles.Admin => ServerRoles.Admin,
            ServerRoles.Editor => ServerRoles.Editor,
            ServerRoles.Viewer => ServerRoles.Viewer,
            _ => throw new ArgumentException($"role 非法：{raw}，必须是 admin/editor/viewer")
        };
    }

    public static string? NormalizeRemoteIp(IPAddress? address)
    {
        if (address is null)
            return null;

        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        return normalized.ToString();
    }

    private static AllowlistEntry Clone(AllowlistEntry source)
    {
        return new AllowlistEntry
        {
            Id = source.Id,
            Ip = source.Ip,
            Name = source.Name,
            Note = source.Note,
            Role = source.Role,
            Enabled = source.Enabled,
            UpdatedAtUtc = source.UpdatedAtUtc
        };
    }

    private sealed class AllowlistState
    {
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
        public List<AllowlistEntry> Entries { get; set; } = [];
    }
}

public sealed class AllowlistEntry
{
    public string Id { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Role { get; set; } = ServerRoles.Viewer;
    public bool Enabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AllowlistSnapshot
{
    public List<AllowlistEntry> Entries { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AccessCheckResult
{
    public bool Allowed { get; set; }
    public string RemoteIp { get; set; } = string.Empty;
    public string? EntryId { get; set; }
    public string? EntryName { get; set; }
    public string? Role { get; set; }
    public string? Note { get; set; }
    public string? Reason { get; set; }
}

public sealed class AllowlistMutationRequest
{
    public string? Ip { get; init; }
    public string? Name { get; init; }
    public string? Note { get; init; }
    public string? Role { get; init; }
    public bool? Enabled { get; init; }
}
