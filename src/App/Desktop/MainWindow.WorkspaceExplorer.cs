using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Dna.App.Desktop;

public partial class MainWindow
{
    private string? _selectedWorkspaceEntryPath;

    private void InitializeWorkspaceExplorer()
    {
        ResetWorkspaceExplorer();
    }

    private async void RefreshWorkspaceTree_OnClick(object? sender, RoutedEventArgs e)
    {
        await RefreshWorkspaceTreeAsync();
    }

    private async Task RefreshWorkspaceTreeAsync()
    {
        if (_project is null)
        {
            ResetWorkspaceExplorer();
            return;
        }

        WorkspaceTreeSummaryText.Text = "Loading WorkspaceEngine tree...";
        WorkspaceProjectPathText.Text = _project.ProjectRoot;

        try
        {
            var tree = await GetJsonAsync(BuildLocalUrl("/api/workspace/tree?maxDepth=6"));
            var root = ParseWorkspaceDirectoryTree(tree);
            var items = root.Children;

            WorkspaceTreeView.ItemsSource = items;
            WorkspaceTreeSummaryText.Text =
                $"{root.Name}  |  {root.DirectoryCount} dirs  |  {root.FileCount} files  |  updated {root.ScannedAtUtc.ToLocalTime():HH:mm:ss}";
            WorkspaceProjectPathText.Text = root.FullPath;

            var preferred = FindWorkspaceTreeItem(items, _selectedWorkspaceEntryPath);
            if (preferred is not null)
            {
                WorkspaceTreeView.SelectedItem = preferred;
                ApplyWorkspaceSelection(preferred);
                return;
            }

            ResetWorkspaceSelectionDetails("Select a folder or file from the WorkspaceEngine tree.");
        }
        catch (Exception ex)
        {
            WorkspaceTreeView.ItemsSource = Array.Empty<WorkspaceTreeItemViewModel>();
            WorkspaceTreeSummaryText.Text = "Workspace tree unavailable";
            WorkspaceProjectPathText.Text = ex.Message;
            ResetWorkspaceSelectionDetails($"Workspace tree load failed: {ex.Message}");
        }
    }

    private void WorkspaceTreeView_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceTreeView.SelectedItem is not WorkspaceTreeItemViewModel item)
        {
            _selectedWorkspaceEntryPath = null;
            ResetWorkspaceSelectionDetails("Select a folder or file from the WorkspaceEngine tree.");
            return;
        }

        _selectedWorkspaceEntryPath = item.Path;
        ApplyWorkspaceSelection(item);
    }

    private void ResetWorkspaceExplorer()
    {
        WorkspaceTreeView.ItemsSource = Array.Empty<WorkspaceTreeItemViewModel>();
        WorkspaceTreeSummaryText.Text = "WorkspaceEngine Snapshot";
        WorkspaceProjectPathText.Text = "No workspace selected.";
        ResetWorkspaceSelectionDetails("Select a project to load the physical folder tree.");
    }

    private void ResetWorkspaceSelectionDetails(string meta)
    {
        WorkspaceSelectionTitleText.Text = "Nothing selected";
        WorkspaceSelectionMetaText.Text = meta;
        WorkspaceSelectionPathText.Text = "-";
        WorkspaceSelectionBadgeText.Text = string.Empty;
        WorkspaceSelectionActionText.Text = string.Empty;
    }

    private void ApplyWorkspaceSelection(WorkspaceTreeItemViewModel item)
    {
        WorkspaceSelectionTitleText.Text = item.DisplayName;
        WorkspaceSelectionMetaText.Text = item.MetaLine;
        WorkspaceSelectionPathText.Text = string.IsNullOrWhiteSpace(item.Path) ? item.FullPath : item.Path;
        WorkspaceSelectionBadgeText.Text = item.BadgeLine ?? string.Empty;
        WorkspaceSelectionActionText.Text = item.ActionLine ?? string.Empty;
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
            Icon: isDirectory ? "📁" : "📄",
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

    private sealed record WorkspaceTreeRootViewModel(
        string Name,
        string FullPath,
        int DirectoryCount,
        int FileCount,
        DateTime ScannedAtUtc,
        List<WorkspaceTreeItemViewModel> Children);

    private sealed record WorkspaceTreeItemViewModel(
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
}
