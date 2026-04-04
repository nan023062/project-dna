using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class MemoryViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    [ObservableProperty]
    private string _memoryAccessText = "当前记忆库位于项目 .agentic-os；写入能力由本地运行时状态决定。";

    [ObservableProperty]
    private string _memoryStatusText = "记忆区就绪。";

    [ObservableProperty]
    private bool _isAddMemoryEnabled = false;

    [ObservableProperty]
    private string _memoryContent = string.Empty;

    [ObservableProperty]
    private string _memoryDiscipline = "engineering";

    [ObservableProperty]
    private string _memoryTags = string.Empty;

    [ObservableProperty]
    private int _memoryTypeSelectedIndex = 2; // Episodic

    [ObservableProperty]
    private ObservableCollection<string> _memories = new();

    public MemoryViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public void Reset()
    {
        MemoryAccessText = "当前记忆库位于项目 .agentic-os；写入能力由本地运行时状态决定。";
        MemoryStatusText = "记忆区就绪。";
        IsAddMemoryEnabled = false;
        Memories.Clear();
    }

    public void ApplyConnectionState(MainWindow.ConnectionAccessState access)
    {
        if (!access.HasProject)
        {
            MemoryAccessText = "当前记忆库位于项目 .agentic-os；写入能力由本地运行时状态决定。";
            IsAddMemoryEnabled = false;
            return;
        }

        if (!access.RuntimeOnline)
        {
            MemoryAccessText = "运行时离线时，记忆页仅保留浏览入口，本地写入已禁用。";
            IsAddMemoryEnabled = false;
            return;
        }

        if (!access.Allowed)
        {
            MemoryAccessText = "当前运行态为只读，记忆页仅用于查看，本地写入已禁用。";
            IsAddMemoryEnabled = false;
            return;
        }

        if (string.Equals(access.Role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            MemoryAccessText = "当前运行态允许直接写入本地记忆库。";
            IsAddMemoryEnabled = true;
            return;
        }

        MemoryAccessText = $"当前模式为 {access.Role}，此版本只开放本地知识浏览，写入按钮已禁用。";
        IsAddMemoryEnabled = false;
    }

    [RelayCommand]
    public async Task RefreshMemoriesAsync(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            MemoryStatusText = "未选择项目，无法加载记忆。";
            Memories.Clear();
            return;
        }

        try
        {
            var result = await _apiClient.GetAsync("/api/memory/query?limit=40&offset=0");
            var entries = new List<(DateTime createdAt, string line)>();

            if (result.ValueKind == JsonValueKind.Array)
            {
                foreach (var memory in result.EnumerateArray())
                {
                    entries.Add((ParseDate(memory, "createdAt"), BuildMemoryLine(memory)));
                }
            }

            var ordered = entries.OrderByDescending(x => x.createdAt).Select(x => x.line).ToList();
            Memories.Clear();
            foreach (var item in ordered)
            {
                Memories.Add(item);
            }
            MemoryStatusText = $"已加载 {ordered.Count} 条记忆。";
        }
        catch (Exception ex)
        {
            MemoryStatusText = $"记忆加载失败：{ex.Message}";
            Memories.Clear();
        }
    }

    [RelayCommand]
    public async Task AddMemoryAsync(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            MemoryStatusText = "未选择项目，无法写入。";
            return;
        }

        if (!IsAddMemoryEnabled)
        {
            MemoryStatusText = "当前运行态未开放写入，本地记忆写入已禁用。";
            return;
        }

        var content = (MemoryContent ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        var discipline = (MemoryDiscipline ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(discipline))
            discipline = "engineering";

        var type = ResolveMemoryTypeValue();
        var tags = ParseTags(MemoryTags);

        try
        {
            var payload = new
            {
                type,
                tags,
                discipline,
                content
            };

            var saved = await _apiClient.PostAsync("/api/memory/remember", payload);
            var id = GetString(saved, "id", "-");

            MemoryContent = string.Empty;
            MemoryStatusText = $"本地记忆写入成功：{id}";
            await RefreshMemoriesAsync(projectRoot);
        }
        catch (Exception ex)
        {
            MemoryStatusText = $"写入失败：{ex.Message}";
        }
    }

    private static string BuildMemoryLine(JsonElement memory)
    {
        var createdAt = ParseDate(memory, "createdAt");
        var time = createdAt == DateTime.MinValue ? "--" : createdAt.ToLocalTime().ToString("MM-dd HH:mm");
        var type = GetMemoryTypeLabel(memory.TryGetProperty("type", out var typeValue) ? typeValue.ToString() : "-");
        var summary = GetString(memory, "summary", null);
        if (string.IsNullOrWhiteSpace(summary))
            summary = GetString(memory, "content", "(空内容)");

        if (!string.IsNullOrWhiteSpace(summary) && summary.Length > 120)
            summary = summary[..120] + "...";

        return $"{time} | {type} | {summary}";
    }

    private static string GetMemoryTypeLabel(string raw)
    {
        return raw switch
        {
            "0" or "Structural" => "Structural",
            "1" or "Semantic" => "Semantic",
            "2" or "Episodic" => "Episodic",
            "3" or "Working" => "Working",
            "4" or "Procedural" => "Procedural",
            _ => raw
        };
    }

    private int ResolveMemoryTypeValue()
    {
        return MemoryTypeSelectedIndex switch
        {
            0 => 0, // Structural
            1 => 1, // Semantic
            2 => 2, // Episodic
            3 => 3, // Working
            4 => 4, // Procedural
            _ => 2
        };
    }

    private static List<string> ParseTags(string? raw)
    {
        var tags = (raw ?? string.Empty)
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.StartsWith('#') ? tag : $"#{tag}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
            tags.Add("#desktop-note");

        return tags;
    }

    private static DateTime ParseDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var date))
            return date;
        return DateTime.MinValue;
    }

    private static string? GetString(JsonElement element, string propertyName, string? fallback = null)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return fallback;
    }
}
