using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;
using Dna.Knowledge;
using Dna.Memory.Models;

namespace Dna.App.Desktop.ViewModels;

public partial class MemoryViewModel : ObservableObject
{
    private readonly IDesktopLocalWorkbenchClient _localWorkbenchClient;

    [ObservableProperty]
    private string _memoryAccessText = "Current memory store lives in project .agentic-os; write access is decided by the local desktop runtime.";

    [ObservableProperty]
    private string _memoryStatusText = "Memory panel ready.";

    [ObservableProperty]
    private bool _isAddMemoryEnabled;

    [ObservableProperty]
    private string _memoryContent = string.Empty;

    [ObservableProperty]
    private string _memoryDiscipline = "engineering";

    [ObservableProperty]
    private string _memoryTags = string.Empty;

    [ObservableProperty]
    private int _memoryTypeSelectedIndex = 2;

    [ObservableProperty]
    private ObservableCollection<string> _memories = [];

    public MemoryViewModel(IDesktopLocalWorkbenchClient localWorkbenchClient)
    {
        _localWorkbenchClient = localWorkbenchClient;
    }

    public void Reset()
    {
        MemoryAccessText = "Current memory store lives in project .agentic-os; write access is decided by the local desktop runtime.";
        MemoryStatusText = "Memory panel ready.";
        IsAddMemoryEnabled = false;
        Memories.Clear();
    }

    public void ApplyConnectionState(MainWindow.ConnectionAccessState access)
    {
        if (!access.HasProject)
        {
            MemoryAccessText = "Current memory store lives in project .agentic-os; write access is decided by the local desktop runtime.";
            IsAddMemoryEnabled = false;
            return;
        }

        if (!access.RuntimeOnline)
        {
            MemoryAccessText = "Runtime is offline. Memory panel stays browse-only and local writes are disabled.";
            IsAddMemoryEnabled = false;
            return;
        }

        if (!access.Allowed)
        {
            MemoryAccessText = "Current runtime mode is read-only. Memory panel stays browse-only and local writes are disabled.";
            IsAddMemoryEnabled = false;
            return;
        }

        if (string.Equals(access.Role, "admin", StringComparison.OrdinalIgnoreCase))
        {
            MemoryAccessText = "Current runtime mode allows direct local memory writes.";
            IsAddMemoryEnabled = true;
            return;
        }

        MemoryAccessText = $"Current mode is {access.Role}. This MVP only opens local knowledge browsing; memory writes stay disabled.";
        IsAddMemoryEnabled = false;
    }

    [RelayCommand]
    public async Task RefreshMemoriesAsync(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            MemoryStatusText = "No project selected.";
            Memories.Clear();
            return;
        }

        try
        {
            var result = await _localWorkbenchClient.QueryMemoriesAsync(limit: 40, offset: 0);
            var ordered = result
                .OrderByDescending(static memory => memory.CreatedAt)
                .Select(BuildMemoryLine)
                .ToList();

            Memories.Clear();
            foreach (var item in ordered)
                Memories.Add(item);

            MemoryStatusText = $"Loaded {ordered.Count} memories.";
        }
        catch (Exception ex)
        {
            MemoryStatusText = $"Memory load failed: {ex.Message}";
            Memories.Clear();
        }
    }

    [RelayCommand]
    public async Task AddMemoryAsync(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            MemoryStatusText = "No project selected.";
            return;
        }

        if (!IsAddMemoryEnabled)
        {
            MemoryStatusText = "Current runtime mode does not allow local memory writes.";
            return;
        }

        var content = (MemoryContent ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        var discipline = string.IsNullOrWhiteSpace(MemoryDiscipline)
            ? "engineering"
            : MemoryDiscipline.Trim();

        try
        {
            var saved = await _localWorkbenchClient.RememberAsync(new RememberRequest
            {
                Type = (MemoryType)ResolveMemoryTypeValue(),
                NodeType = NodeType.Technical,
                Source = MemorySource.Human,
                Content = content,
                Summary = content.Length > 80 ? content[..80] : content,
                Disciplines = [discipline],
                Tags = ParseTags(MemoryTags),
                Stage = MemoryStage.ShortTerm
            });

            MemoryContent = string.Empty;
            MemoryStatusText = $"Local memory saved: {saved.Id}";
            await RefreshMemoriesAsync(projectRoot);
        }
        catch (Exception ex)
        {
            MemoryStatusText = $"Write failed: {ex.Message}";
        }
    }

    private static string BuildMemoryLine(MemoryEntry memory)
    {
        var time = memory.CreatedAt == DateTime.MinValue
            ? "--"
            : memory.CreatedAt.ToLocalTime().ToString("MM-dd HH:mm");

        var summary = string.IsNullOrWhiteSpace(memory.Summary)
            ? memory.Content
            : memory.Summary;

        if (!string.IsNullOrWhiteSpace(summary) && summary.Length > 120)
            summary = summary[..120] + "...";

        return $"{time} | {memory.Type} | {summary}";
    }

    private int ResolveMemoryTypeValue()
    {
        return MemoryTypeSelectedIndex switch
        {
            0 => (int)MemoryType.Structural,
            1 => (int)MemoryType.Semantic,
            2 => (int)MemoryType.Episodic,
            3 => (int)MemoryType.Working,
            4 => (int)MemoryType.Procedural,
            _ => (int)MemoryType.Episodic
        };
    }

    private static List<string> ParseTags(string? raw)
    {
        var tags = (raw ?? string.Empty)
            .Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static tag => tag.StartsWith('#') ? tag : $"#{tag}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tags.Count == 0)
            tags.Add("#desktop-note");

        return tags;
    }
}
