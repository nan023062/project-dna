using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;
using Dna.Knowledge.Workspace.Models;

namespace Dna.App.Desktop.ViewModels;

public partial class WorkspaceExplorerViewModel : ObservableObject
{
    private readonly IDesktopLocalWorkbenchClient _localWorkbenchClient;

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
    private string _selectionMeta = "Select a folder from the WorkspaceEngine tree.";

    [ObservableProperty]
    private string _selectionPath = "-";

    [ObservableProperty]
    private string _selectionBadge = string.Empty;

    [ObservableProperty]
    private string _selectionAction = string.Empty;

    public WorkspaceExplorerViewModel(IDesktopLocalWorkbenchClient localWorkbenchClient)
    {
        _localWorkbenchClient = localWorkbenchClient;
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
            ResetSelectionDetails("Select a folder from the WorkspaceEngine tree.");
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
            var root = await _localWorkbenchClient.GetWorkspaceTreeAsync(maxDepth: 6);
            
            Items.Clear();
            foreach (var item in root.Entries.Select(ParseWorkspaceTreeItem))
            {
                Items.Add(item);
            }

            SummaryText = $"{root.Name}  |  {root.DirectoryCount} dirs  |  updated {root.ScannedAtUtc.ToLocalTime():HH:mm:ss}";
            ProjectPathText = root.FullPath;

            var preferredPath = SelectedItem?.Path;
            var preferred = FindWorkspaceTreeItem(Items, preferredPath);
            SelectedItem = preferred;
            
            if (preferred is null)
            {
                ResetSelectionDetails("Select a folder from the WorkspaceEngine tree.");
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

    private static WorkspaceTreeItemViewModel ParseWorkspaceTreeItem(WorkspaceFileNode entry)
    {
        var isDirectory = entry.Kind == WorkspaceEntryKind.Directory;
        var directoryCount = entry.ChildDirectoryCount;
        var fileCount = entry.ChildFileCount;
        var module = ParseWorkspaceModuleLabel(entry);
        var descriptor = ParseWorkspaceDescriptorLabel(entry);
        var badge = entry.Badge;
        var statusLabel = entry.StatusLabel;
        var sizeBytes = entry.SizeBytes;
        var lastModified = entry.LastModifiedUtc ?? DateTime.MinValue;
        var children = entry.Children?.Select(ParseWorkspaceTreeItem).ToList() ?? [];

        var meta = isDirectory
            ? $"{statusLabel} | {directoryCount} dirs"
            : $"{statusLabel} | {FormatFileSize(sizeBytes)}";

        if (lastModified != DateTime.MinValue)
            meta = $"{meta} | {lastModified.ToLocalTime():MM-dd HH:mm}";

        var actionParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(module))
            actionParts.Add(module);
        if (!string.IsNullOrWhiteSpace(descriptor))
            actionParts.Add(descriptor);
        if (TryBuildWorkspaceActionHint(entry, out var actionHint))
            actionParts.Add(actionHint);

        return new WorkspaceTreeItemViewModel(
            Name: entry.Name,
            DisplayName: entry.Name,
            Path: entry.Path,
            FullPath: entry.FullPath,
            IsDirectory: isDirectory,
            MetaLine: meta,
            Caption: isDirectory ? BuildFolderCaption(directoryCount, fileCount, badge) : (badge ?? statusLabel),
            BadgeLine: badge,
            ActionLine: actionParts.Count == 0 ? null : string.Join(" | ", actionParts),
            Children: children);
    }

    private static string BuildFolderCaption(int directoryCount, int fileCount, string? badge)
    {
        _ = fileCount;
        var caption = $"{directoryCount} dirs";
        return string.IsNullOrWhiteSpace(badge)
            ? caption
            : $"{caption} | {badge}";
    }

    private static string? ParseWorkspaceModuleLabel(WorkspaceFileNode entry)
    {
        if (entry.Module is null)
            return null;

        return $"Module: {entry.Module.Name} ({entry.Module.Discipline}, L{entry.Module.Layer})";
    }

    private static string? ParseWorkspaceDescriptorLabel(WorkspaceFileNode entry)
    {
        if (entry.Descriptor is null)
            return null;

        var stableGuid = entry.Descriptor.StableGuid;
        return string.IsNullOrWhiteSpace(stableGuid)
            ? "Metadata: .agentic.meta"
            : $"Metadata: {stableGuid}";
    }

    private static bool TryBuildWorkspaceActionHint(WorkspaceFileNode entry, out string actionHint)
    {
        actionHint = string.Empty;
        var hints = new List<string>();
        if (entry.Actions.CanEdit)
            hints.Add("editable");
        if (entry.Actions.CanRegister)
            hints.Add("registerable");

        if (hints.Count == 0)
            return false;

        actionHint = $"Actions: {string.Join(", ", hints)}";
        return true;
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
}

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
    List<WorkspaceTreeItemViewModel> Children);
