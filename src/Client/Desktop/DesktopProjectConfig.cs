using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Dna.Client.Desktop;

public sealed class DesktopProjectConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public required string ProjectRoot { get; init; }
    public required string ProjectName { get; init; }
    public required string ServerBaseUrl { get; init; }
    public required string ConfigPath { get; init; }
    public required string WorkspaceConfigPath { get; init; }

    public static DesktopProjectConfig Load(string projectRoot)
    {
        var normalizedRoot = Path.GetFullPath(projectRoot);
        if (!Directory.Exists(normalizedRoot))
            throw new InvalidOperationException($"项目目录不存在：{normalizedRoot}");

        var configPath = Path.Combine(normalizedRoot, ".project.dna");
        if (!File.Exists(configPath))
            throw new InvalidOperationException($"未找到 .project.dna：{configPath}");

        ProjectDnaConfig dto;
        try
        {
            var json = File.ReadAllText(configPath);
            dto = JsonSerializer.Deserialize<ProjectDnaConfig>(json, JsonOptions)
                  ?? throw new InvalidOperationException(".project.dna 内容为空。");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($".project.dna JSON 解析失败：{ex.Message}");
        }

        var projectName = (dto.ProjectName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new InvalidOperationException(".project.dna 缺少 projectName。");

        var serverBaseUrl = NormalizeAndValidateServerUrl(dto.ServerBaseUrl);

        return new DesktopProjectConfig
        {
            ProjectRoot = normalizedRoot,
            ProjectName = projectName,
            ServerBaseUrl = serverBaseUrl,
            ConfigPath = configPath,
            WorkspaceConfigPath = ResolveWorkspaceConfigPath(normalizedRoot)
        };
    }

    public void EnsureWorkspaceConfig()
    {
        var mode = InferWorkspaceMode(ServerBaseUrl);
        var state = new WorkspaceConfigState
        {
            CurrentWorkspaceId = "default",
            Workspaces =
            [
                new WorkspaceConfigRecord
                {
                    Id = "default",
                    Name = ProjectName,
                    Mode = mode,
                    ServerBaseUrl = ServerBaseUrl,
                    WorkspaceRoot = ProjectRoot,
                    UpdatedAtUtc = DateTime.UtcNow
                }
            ]
        };

        var dir = Path.GetDirectoryName(WorkspaceConfigPath)
                  ?? throw new InvalidOperationException("无法确定 workspace 配置目录。");
        Directory.CreateDirectory(dir);
        File.WriteAllText(WorkspaceConfigPath, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static string NormalizeAndValidateServerUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(".project.dna 缺少 serverBaseUrl。");

        var normalized = raw.Trim().TrimEnd('/');
        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"serverBaseUrl 不是合法绝对地址：{raw}");

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("serverBaseUrl 仅支持 http 或 https。");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }

    private static string InferWorkspaceMode(string serverBaseUrl)
    {
        if (!Uri.TryCreate(serverBaseUrl, UriKind.Absolute, out var uri))
            return "team";

        return uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? "personal"
            : "team";
    }

    private static string ResolveWorkspaceConfigPath(string projectRoot)
    {
        var profileRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".dna",
            "client-desktop",
            "projects");

        var projectId = ComputeStableId(projectRoot);
        return Path.Combine(profileRoot, projectId, "client-workspaces.json");
    }

    private static string ComputeStableId(string projectRoot)
    {
        var normalized = Path.GetFullPath(projectRoot).ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }

    private sealed class ProjectDnaConfig
    {
        public string? ProjectName { get; init; }
        public string? ServerBaseUrl { get; init; }
    }

    private sealed class WorkspaceConfigState
    {
        public string CurrentWorkspaceId { get; init; } = "default";
        public List<WorkspaceConfigRecord> Workspaces { get; init; } = [];
    }

    private sealed class WorkspaceConfigRecord
    {
        public string Id { get; init; } = "default";
        public string Name { get; init; } = string.Empty;
        public string Mode { get; init; } = "personal";
        public string ServerBaseUrl { get; init; } = string.Empty;
        public string WorkspaceRoot { get; init; } = string.Empty;
        public DateTime UpdatedAtUtc { get; init; }
    }
}
