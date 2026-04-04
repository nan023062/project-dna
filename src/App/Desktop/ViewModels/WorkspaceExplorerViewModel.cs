using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class WorkspaceExplorerViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    [ObservableProperty]
    private ObservableCollection<WorkspaceTreeItemViewModel> _items = new();

    [ObservableProperty]
    private WorkspaceTreeItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _summaryText = "WorkspaceEngine Snapshot";

    [ObservableProperty]
    private string _projectPathText = "No workspace selected.";

    [ObservableProperty]
    private string _selectionTitle = "Nothing selected";

    [ObservableProperty]
    private string _selectionMeta = "Select a folder or file from the WorkspaceEngine tree.";

    [ObservableProperty]
    private string _selectionPath = "-";

    [ObservableProperty]
    private string _selectionBadge = string.Empty;

    [ObservableProperty]
    private string _selectionAction = string.Empty;

    public WorkspaceExplorerViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public void Reset()
    {
        Items.Clear();
        SummaryText = "WorkspaceEngine Snapshot";
        ProjectPathText = "No workspace selected.";
        SelectedItem = null;
        ResetSelectionDetails("Select a project to load the physical folder tree.");
    }

    private void ResetSelectionDetails(string meta)
    {
        SelectionTitle = "Nothing selected";
        SelectionMeta = meta;
        SelectionPath = "-";
        SelectionBadge = string.Empty;
        SelectionAction = string.Empty;
    }

    partial void OnSelectedItemChanged(WorkspaceTreeItemViewModel? value)
    {
        if (value is null)
        {
            ResetSelectionDetails("Select a folder or file from the WorkspaceEngine tree.");
            return;
        }

        SelectionTitle = value.DisplayName;
        SelectionMeta = value.MetaLine;
        SelectionPath = string.IsNullOrWhiteSpace(value.Path) ? value.FullPath : value.Path;
        SelectionBadge = value.BadgeLine ?? string.Empty;
        SelectionAction = value.ActionLine ?? string.Empty;
    }

    [RelayCommand]
    public async Task RefreshAsync(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Reset();
            return;
        }

        SummaryText = "Loading WorkspaceEngine tree...";
        ProjectPathText = projectRoot;

        try
        {
            var tree = await _apiClient.GetAsync("/api/workspace/tree?maxDepth=6");
            var root = ParseWorkspaceDirectoryTree(tree);
            
            Items.Clear();
            foreach (var item in root.Children)
            {
                Items.Add(item);
            }

            SummaryText = $"{root.Name}  |  {root.DirectoryCount} dirs  |  {root.FileCount} files  |  updated {root.ScannedAtUtc.ToLocalTime():HH:mm:ss}";
            ProjectPathText = root.FullPath;

            var preferredPath = SelectedItem?.Path;
            var preferred = FindWorkspaceTreeItem(Items, preferredPath);
            SelectedItem = preferred;
            
            if (preferred is null)
            {
                ResetSelectionDetails("Select a folder or file from the WorkspaceEngine tree.");
            }
        }
        catch (Exception ex)
        {
            Items.Clear();
            SummaryText = "Workspace tree unavailable";
            ProjectPathText = ex.Message;
            SelectedItem = null;
            ResetSelectionDetails($"Workspace tree load failed: {ex.Message}");
        }
    }

    private static WorkspaceTreeRootViewModel ParseWorkspaceDirectoryTree(JsonElement element)
    {
        return new WorkspaceTreeRootViewModel(
            Name: GetString(element, "name", "Workspace") ?? "Workspace",
            FullPath: GetString(element, "fullPath", string.Empty) ?? string.Empty,
            DirectoryCount: ParseNullableInt(element, "directoryCount") ?? 0,
            FileCount: ParseNullableInt(element, "fileCount") ?? 0,
            ScannedAtUtc: ParseDate(element, "scannedAtUtc"),
            Children: ParseWorkspaceTreeItems(element, "entries"));
    }

    private static List<WorkspaceTreeItemViewModel> ParseWorkspaceTreeItems(JsonElement element, string propertyName)
    {
        var items = new List<WorkspaceTreeItemViewModel>();
        if (!element.TryGetProperty(propertyName, out var entries) || entries.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var entry in entries.EnumerateArray())
            items.Add(ParseWorkspaceTreeItem(entry));

        return items;
    }

    private static WorkspaceTreeItemViewModel ParseWorkspaceTreeItem(JsonElement element)
    {
        var kind = GetString(element, "kind", "Directory") ?? "Directory";
        var isDirectory = string.Equals(kind, "Directory", StringComparison.OrdinalIgnoreCase);
        var directoryCount = ParseNullableInt(element, "childDirectoryCount") ?? 0;
        var fileCount = ParseNullableInt(element, "childFileCount") ?? 0;
        var module = ParseWorkspaceModuleLabel(element);
        var descriptor = ParseWorkspaceDescriptorLabel(element);
        var badge = GetString(element, "badge", null);
        var statusLabel = GetString(element, "statusLabel", "-") ?? "-";
        var sizeBytes = ParseInt64(element, "sizeBytes");
        var lastModified = ParseDate(element, "lastModifiedUtc");
        var children = ParseWorkspaceTreeItems(element, "children");

        var meta = isDirectory
            ? $"{statusLabel} | {directoryCount} dirs | {fileCount} files"
            : $"{statusLabel} | {FormatFileSize(sizeBytes)}";

        if (lastModified != DateTime.MinValue)
            meta = $"{meta} | {lastModified.ToLocalTime():MM-dd HH:mm}";

        var actionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(module))
            actionParts.Add(module);
        if (!string.IsNullOrWhiteSpace(descriptor))
            actionParts.Add(descriptor);
        if (TryBuildWorkspaceActionHint(element, out var actionHint))
            actionParts.Add(actionHint);

        return new WorkspaceTreeItemViewModel(
            Name: GetString(element, "name", string.Empty) ?? string.Empty,
            DisplayName: GetString(element, "name", string.Empty) ?? string.Empty,
            Path: GetString(element, "path", string.Empty) ?? string.Empty,
            FullPath: GetString(element, "fullPath", string.Empty) ?? string.Empty,
            IsDirectory: isDirectory,
            MetaLine: meta,
            Caption: isDirectory ? BuildFolderCaption(directoryCount, fileCount, badge) : (badge ?? statusLabel),
            BadgeLine: badge,
            ActionLine: actionParts.Count == 0 ? null : string.Join(" | ", actionParts),
            Icon: isDirectory ? "D" : "F",
            Children: children);
    }

    private static string BuildFolderCaption(int directoryCount, int fileCount, string? badge)
    {
        var caption = $"{directoryCount} dirs / {fileCount} files";
        return string.IsNullOrWhiteSpace(badge)
            ? caption
            : $"{caption} | {badge}";
    }

    private static string? ParseWorkspaceModuleLabel(JsonElement element)
    {
        if (!element.TryGetProperty("module", out var module) || module.ValueKind != JsonValueKind.Object)
            return null;

        var name = GetString(module, "name", null);
        var discipline = GetString(module, "discipline", null);
        var layer = ParseNullableInt(module, "layer");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return layer.HasValue
            ? $"Module: {name} ({discipline ?? "-"}, L{layer.Value})"
            : $"Module: {name}";
    }

    private static string? ParseWorkspaceDescriptorLabel(JsonElement element)
    {
        if (!element.TryGetProperty("descriptor", out var descriptor) || descriptor.ValueKind != JsonValueKind.Object)
            return null;

        var stableGuid = GetString(descriptor, "stableGuid", null);
        return string.IsNullOrWhiteSpace(stableGuid)
            ? "Metadata: .agentic.meta"
            : $"Metadata: {stableGuid}";
    }

    private static bool TryBuildWorkspaceActionHint(JsonElement element, out string actionHint)
    {
        actionHint = string.Empty;
        if (!element.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Object)
            return false;

        var hints = new List<string>();
        if (actions.TryGetProperty("canEdit", out var canEdit) && canEdit.ValueKind == JsonValueKind.True)
            hints.Add("editable");
        if (actions.TryGetProperty("canRegister", out var canRegister) && canRegister.ValueKind == JsonValueKind.True)
            hints.Add("registerable");

        if (hints.Count == 0)
            return false;

        actionHint = $"Actions: {string.Join(", ", hints)}";
        return true;
    }

    private static long? ParseInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatFileSize(long? sizeBytes)
    {
        if (!sizeBytes.HasValue || sizeBytes.Value < 0)
            return "-";

        var size = sizeBytes.Value;
        if (size < 1024)
            return $"{size} B";
        if (size < 1024 * 1024)
            return $"{size / 1024d:0.#} KB";
        if (size < 1024 * 1024 * 1024)
            return $"{size / 1024d / 1024d:0.#} MB";

        return $"{size / 1024d / 1024d / 1024d:0.#} GB";
    }

    private static WorkspaceTreeItemViewModel? FindWorkspaceTreeItem(
        IEnumerable<WorkspaceTreeItemViewModel> items,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        foreach (var item in items)
        {
            if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase))
                return item;

            var nested = FindWorkspaceTreeItem(item.Children, path);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName, string? defaultValue)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return defaultValue;
    }

    private static int? ParseNullableInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return null;
    }

    private static DateTime ParseDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var date))
            return date;
        return DateTime.MinValue;
    }
}

public sealed record WorkspaceTreeRootViewModel(
    string Name,
    string FullPath,
    int DirectoryCount,
    int FileCount,
    DateTime ScannedAtUtc,
    List<WorkspaceTreeItemViewModel> Children);

public sealed record WorkspaceTreeItemViewModel(
    string Name,
    string DisplayName,
    string Path,
    string FullPath,
    bool IsDirectory,
    string MetaLine,
    string Caption,
    string? BadgeLine,
    string? ActionLine,
    string Icon,
    List<WorkspaceTreeItemViewModel> Children);
