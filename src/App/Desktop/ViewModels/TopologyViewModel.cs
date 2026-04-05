using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;
using Dna.Knowledge;

namespace Dna.App.Desktop.ViewModels;

public partial class TopologyViewModel : ObservableObject
{
    private readonly IDesktopLocalWorkbenchClient _localWorkbenchClient;
    private readonly Dictionary<string, TopologyWorkbenchModuleView> _modulesById = new(StringComparer.OrdinalIgnoreCase);
    private string? _projectRoot;

    public event Action? GraphChanged;
    public event Action? NavigateRootRequested;
    public event Action<string>? CopyMcpRequested;

    [ObservableProperty]
    private ObservableCollection<TopologyNodeViewModel> _graphNodes = [];

    [ObservableProperty]
    private ObservableCollection<TopologyEdgeViewModel> _graphEdges = [];

    [ObservableProperty]
    private ObservableCollection<string> _visibleNodeLines = [];

    [ObservableProperty]
    private ObservableCollection<string> _relationLines = [];

    [ObservableProperty]
    private ObservableCollection<string> _disciplineOptions = [];

    [ObservableProperty]
    private ObservableCollection<TopologyParentOptionViewModel> _parentOptions = [];

    [ObservableProperty]
    private string _topologySummaryText = "Topology not loaded.";

    [ObservableProperty]
    private string _topologyScopeText = "Current scope: root";

    [ObservableProperty]
    private string _statModulesText = "-";

    [ObservableProperty]
    private string _statDependenciesText = "-";

    [ObservableProperty]
    private string _statCollaborationText = "-";

    [ObservableProperty]
    private string _statDisciplinesText = "-";

    [ObservableProperty]
    private string _detailTitle = "No node selected";

    [ObservableProperty]
    private string _detailMeta = "-";

    [ObservableProperty]
    private string _detailSummary = "Select a node in the graph.";

    [ObservableProperty]
    private string _editHintText = "Select a module node to edit its definition.";

    [ObservableProperty]
    private string _editParallelHintText = "Manual save and MCP upsert_module share the same write path.";

    [ObservableProperty]
    private string _editDiscipline = string.Empty;

    [ObservableProperty]
    private string _editLayer = string.Empty;

    [ObservableProperty]
    private string _editName = string.Empty;

    [ObservableProperty]
    private string _editPath = string.Empty;

    [ObservableProperty]
    private TopologyParentOptionViewModel? _selectedParent;

    [ObservableProperty]
    private string _editManagedPaths = string.Empty;

    [ObservableProperty]
    private string _editMaintainer = string.Empty;

    [ObservableProperty]
    private string _editSummary = string.Empty;

    [ObservableProperty]
    private string _editBoundary = string.Empty;

    [ObservableProperty]
    private string _editDependencies = string.Empty;

    [ObservableProperty]
    private string _editPublicApi = string.Empty;

    [ObservableProperty]
    private string _editConstraints = string.Empty;

    [ObservableProperty]
    private string _editWorkflow = string.Empty;

    [ObservableProperty]
    private string _editRules = string.Empty;

    [ObservableProperty]
    private string _editProhibitions = string.Empty;

    [ObservableProperty]
    private string _editMetadata = "{}";

    [ObservableProperty]
    private string _mcpPreview = string.Empty;

    [ObservableProperty]
    private string _editStatusText = "Module editor ready.";

    [ObservableProperty]
    private string _knowledgeHintText = "Select a real module to edit knowledge.";

    [ObservableProperty]
    private string _knowledgeParallelHintText = "Manual save and MCP/CLI writes share the same storage path.";

    [ObservableProperty]
    private string _knowledgeIdentity = string.Empty;

    [ObservableProperty]
    private string _knowledgeLessons = string.Empty;

    [ObservableProperty]
    private string _knowledgeActiveTasks = string.Empty;

    [ObservableProperty]
    private string _knowledgeFacts = string.Empty;

    [ObservableProperty]
    private string _knowledgeStatusText = "Knowledge editor ready.";

    [ObservableProperty]
    private bool _showDependency = true;

    [ObservableProperty]
    private bool _showComposition = true;

    [ObservableProperty]
    private bool _showAggregation = true;

    [ObservableProperty]
    private bool _showParentChild = true;

    [ObservableProperty]
    private bool _showCollaboration = true;

    [ObservableProperty]
    private string? _selectedNodeId;

    public TopologyViewModel(IDesktopLocalWorkbenchClient localWorkbenchClient)
    {
        _localWorkbenchClient = localWorkbenchClient;
    }

    [RelayCommand]
    public async Task RefreshAsync(string? projectRoot)
    {
        _projectRoot = projectRoot;
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            Reset();
            return;
        }

        try
        {
            var snapshot = await _localWorkbenchClient.GetTopologySnapshotAsync();
            BuildTopologyState(snapshot);
            GraphChanged?.Invoke();

            if (!string.IsNullOrWhiteSpace(SelectedNodeId))
                await SelectNodeAsync(SelectedNodeId);
        }
        catch (Exception ex)
        {
            Reset();
            TopologySummaryText = $"Topology load failed: {ex.Message}";
            EditStatusText = $"Module refresh failed: {ex.Message}";
            KnowledgeStatusText = $"Knowledge refresh failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public void NavigateRoot()
    {
        NavigateRootRequested?.Invoke();
    }

    [RelayCommand]
    public async Task SaveModuleAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
        {
            EditStatusText = "Please select a module first.";
            return;
        }

        try
        {
            var metadata = ParseMetadataJson(EditMetadata)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UpsertMetadataList(metadata, "workflow", ParseMultilineList(EditWorkflow));
            UpsertMetadataList(metadata, "rules", ParseMultilineList(EditRules));
            UpsertMetadataList(metadata, "prohibitions", ParseMultilineList(EditProhibitions));

            await _localWorkbenchClient.SaveModuleAsync(
                string.IsNullOrWhiteSpace(EditDiscipline) ? "engineering" : EditDiscipline.Trim(),
                new TopologyModuleDefinition
                {
                    Discipline = string.IsNullOrWhiteSpace(EditDiscipline) ? "engineering" : EditDiscipline.Trim(),
                    Id = SelectedNodeId,
                    Name = EditName.Trim(),
                    Path = EditPath.Trim(),
                    Layer = int.TryParse(EditLayer, out var layer) ? layer : 0,
                    ParentModuleId = SelectedParent?.NodeId,
                    ManagedPaths = ParseMultilineList(EditManagedPaths),
                    Dependencies = ParseMultilineList(EditDependencies),
                    Maintainer = EmptyToNull(EditMaintainer),
                    Summary = EmptyToNull(EditSummary),
                    Boundary = EmptyToNull(EditBoundary),
                    PublicApi = ParseMultilineList(EditPublicApi),
                    Constraints = ParseMultilineList(EditConstraints),
                    Metadata = metadata
                });

            EditStatusText = $"Module saved: {EditName}";
            await RefreshAsync(_projectRoot);
            await SelectNodeAsync(SelectedNodeId);
        }
        catch (Exception ex)
        {
            EditStatusText = $"Module save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReloadModuleAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
            return;

        await SelectNodeAsync(SelectedNodeId);
        EditStatusText = $"Module reloaded: {EditName}";
    }

    [RelayCommand]
    public void CopyMcpPreview()
    {
        if (!string.IsNullOrWhiteSpace(McpPreview))
            CopyMcpRequested?.Invoke(McpPreview);
    }

    [RelayCommand]
    public async Task SaveKnowledgeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
        {
            KnowledgeStatusText = "Please select a module first.";
            return;
        }

        try
        {
            await _localWorkbenchClient.SaveModuleKnowledgeAsync(new TopologyModuleKnowledgeUpsertCommand
            {
                NodeIdOrName = SelectedNodeId,
                Knowledge = new NodeKnowledge
                {
                    Identity = EmptyToNull(KnowledgeIdentity),
                    Lessons = ParseLessons(KnowledgeLessons),
                    ActiveTasks = ParseMultilineList(KnowledgeActiveTasks),
                    Facts = ParseMultilineList(KnowledgeFacts),
                    TotalMemoryCount = 0,
                    IdentityMemoryId = null,
                    UpgradeTrailMemoryId = null,
                    MemoryIds = []
                }
            });

            KnowledgeStatusText = $"Knowledge saved: {EditName}";
            await SelectNodeAsync(SelectedNodeId);
        }
        catch (Exception ex)
        {
            KnowledgeStatusText = $"Knowledge save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReloadKnowledgeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
            return;

        await LoadKnowledgeAsync(SelectedNodeId);
        KnowledgeStatusText = $"Knowledge reloaded: {EditName}";
    }

    [RelayCommand]
    public void ResetKnowledge()
    {
        KnowledgeIdentity = string.Empty;
        KnowledgeLessons = string.Empty;
        KnowledgeActiveTasks = string.Empty;
        KnowledgeFacts = string.Empty;
        KnowledgeStatusText = "Knowledge form reset.";
    }

    public async Task SelectNodeAsync(string? nodeId)
    {
        SelectedNodeId = nodeId;
        if (string.IsNullOrWhiteSpace(nodeId) || !_modulesById.TryGetValue(nodeId, out var module))
        {
            ResetDetails();
            return;
        }

        var label = string.IsNullOrWhiteSpace(module.DisplayName) ? module.Name : module.DisplayName;
        DetailTitle = label;
        DetailMeta = BuildDetailMeta(module);
        DetailSummary = string.IsNullOrWhiteSpace(module.Summary) ? "No summary." : module.Summary;

        EditDiscipline = module.Discipline;
        EditLayer = module.Layer.ToString();
        EditName = module.Name;
        EditPath = module.RelativePath ?? string.Empty;
        EditManagedPaths = ToMultilineText(module.ManagedPathScopes);
        EditMaintainer = module.Maintainer ?? string.Empty;
        EditSummary = module.Summary ?? string.Empty;
        EditBoundary = module.Boundary ?? string.Empty;
        EditDependencies = ToMultilineText(module.Dependencies);
        EditPublicApi = ToMultilineText(module.PublicApi);
        EditConstraints = ToMultilineText(module.Constraints);
        EditWorkflow = ToMultilineText(ParseMetadataList(module.Metadata, "workflow"));
        EditRules = ToMultilineText(ParseMetadataList(module.Metadata, "rules"));
        EditProhibitions = ToMultilineText(ParseMetadataList(module.Metadata, "prohibitions"));
        EditMetadata = JsonSerializer.Serialize(
            module.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new JsonSerializerOptions { WriteIndented = true });
        McpPreview = BuildMcpPreview();
        EditHintText = $"Editing module: {label}";

        SelectedParent = ParentOptions.FirstOrDefault(item =>
            string.Equals(item.NodeId, module.ParentModuleId, StringComparison.OrdinalIgnoreCase));

        VisibleNodeLines.Clear();
        foreach (var item in GraphNodes.Select(node => $"{node.Label} | {node.TypeLabel} | {node.DisciplineLabel}"))
            VisibleNodeLines.Add(item);

        await LoadKnowledgeAsync(nodeId);
        await LoadRelationsAsync(nodeId);
    }

    public void ApplyScope(
        string? viewRootId,
        IReadOnlyList<string> scopeTrailIds,
        IReadOnlyList<string> visibleNodeIds,
        IReadOnlyList<TopologyEdgeViewModel> visibleEdges)
    {
        _ = scopeTrailIds;
        var scopeRootLabel = string.IsNullOrWhiteSpace(viewRootId)
            ? "root"
            : GraphNodes.FirstOrDefault(node => string.Equals(node.Id, viewRootId, StringComparison.OrdinalIgnoreCase))?.Label ?? viewRootId;
        TopologyScopeText = $"Current scope: {scopeRootLabel}";

        VisibleNodeLines.Clear();
        foreach (var id in visibleNodeIds)
        {
            var node = GraphNodes.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (node is not null)
                VisibleNodeLines.Add($"{node.Label} | {node.TypeLabel} | {node.DisciplineLabel}");
        }

        RelationLines.Clear();
        foreach (var edge in visibleEdges)
            RelationLines.Add($"{edge.Relation}: {edge.From} -> {edge.To}");
    }

    public void Reset()
    {
        GraphNodes.Clear();
        GraphEdges.Clear();
        VisibleNodeLines.Clear();
        RelationLines.Clear();
        DisciplineOptions.Clear();
        ParentOptions.Clear();
        SelectedNodeId = null;
        TopologySummaryText = "Topology not loaded.";
        TopologyScopeText = "Current scope: root";
        StatModulesText = "-";
        StatDependenciesText = "-";
        StatCollaborationText = "-";
        StatDisciplinesText = "-";
        ResetDetails();
    }

    private void BuildTopologyState(TopologyWorkbenchSnapshot snapshot)
    {
        _modulesById.Clear();
        GraphNodes.Clear();
        GraphEdges.Clear();
        DisciplineOptions.Clear();
        ParentOptions.Clear();

        foreach (var module in snapshot.Modules)
        {
            if (string.IsNullOrWhiteSpace(module.NodeId))
                continue;

            _modulesById[module.NodeId] = module;
            GraphNodes.Add(new TopologyNodeViewModel(
                Id: module.NodeId,
                NodeId: module.NodeId,
                Label: string.IsNullOrWhiteSpace(module.DisplayName) ? module.Name : module.DisplayName,
                Type: module.Type,
                TypeLabel: module.TypeLabel,
                Discipline: module.Discipline,
                DisciplineLabel: module.DisciplineDisplayName,
                DependencyCount: module.Dependencies.Count,
                Summary: module.Summary ?? string.Empty,
                ComputedLayer: module.ArchitectureLayerScore,
                RelativePath: module.RelativePath,
                ParentModuleId: module.ParentModuleId,
                ChildModuleIds: module.ChildIds,
                ManagedPathScopes: module.ManagedPathScopes,
                Maintainer: module.Maintainer,
                Boundary: module.Boundary,
                PublicApi: module.PublicApi,
                Constraints: module.Constraints,
                Workflow: ParseMetadataList(module.Metadata, "workflow"),
                Rules: ParseMetadataList(module.Metadata, "rules"),
                Prohibitions: ParseMetadataList(module.Metadata, "prohibitions"),
                Metadata: module.Metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                CanEdit: module.CanEdit));
        }

        var edges = snapshot.RelationEdges.Count > 0 ? snapshot.RelationEdges : snapshot.Edges;
        foreach (var edge in edges)
        {
            GraphEdges.Add(new TopologyEdgeViewModel(
                From: edge.From,
                To: edge.To,
                Relation: edge.Relation,
                IsComputed: edge.IsComputed,
                Kind: edge.Kind));
        }

        foreach (var discipline in GraphNodes
                     .Select(node => node.Discipline)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            DisciplineOptions.Add(discipline);
        }

        ParentOptions.Add(new TopologyParentOptionViewModel(null, "(none)"));
        foreach (var node in GraphNodes.OrderBy(static node => node.Label, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new TopologyParentOptionViewModel(node.NodeId, node.Label));

        TopologySummaryText = string.IsNullOrWhiteSpace(snapshot.Summary)
            ? $"Loaded {GraphNodes.Count} modules."
            : snapshot.Summary;
        TopologyScopeText = "Current scope: root";
        StatModulesText = GraphNodes.Count.ToString();
        StatDependenciesText = GraphEdges.Count(edge => string.Equals(edge.Relation, "dependency", StringComparison.OrdinalIgnoreCase)).ToString();
        StatCollaborationText = GraphEdges.Count(edge => string.Equals(edge.Relation, "collaboration", StringComparison.OrdinalIgnoreCase)).ToString();
        StatDisciplinesText = DisciplineOptions.Count.ToString();
        ApplyScope(null, [], GraphNodes.Select(node => node.Id).ToArray(), GraphEdges.ToArray());
    }

    private async Task LoadKnowledgeAsync(string nodeId)
    {
        try
        {
            var result = await _localWorkbenchClient.GetModuleKnowledgeAsync(nodeId);
            if (result is null)
            {
                KnowledgeStatusText = $"Knowledge not found: {nodeId}";
                return;
            }

            KnowledgeIdentity = result.Knowledge.Identity ?? string.Empty;
            KnowledgeLessons = string.Join(Environment.NewLine, result.Knowledge.Lessons.Select(static lesson => lesson.Title));
            KnowledgeActiveTasks = ToMultilineText(result.Knowledge.ActiveTasks);
            KnowledgeFacts = ToMultilineText(result.Knowledge.Facts);
            KnowledgeHintText = $"Editing module knowledge: {result.Name}";
            KnowledgeStatusText = "Knowledge editor ready.";
        }
        catch (Exception ex)
        {
            KnowledgeStatusText = $"Knowledge load failed: {ex.Message}";
        }
    }

    private async Task LoadRelationsAsync(string nodeId)
    {
        try
        {
            var result = await _localWorkbenchClient.GetModuleRelationsAsync(nodeId);
            RelationLines.Clear();

            if (result is null)
            {
                RelationLines.Add($"Relations not found: {nodeId}");
                return;
            }

            foreach (var relation in result.Outgoing)
                RelationLines.Add($"{relation.Type}: {relation.ToName}");

            foreach (var relation in result.Incoming)
                RelationLines.Add($"{relation.Type}: {relation.FromName}");
        }
        catch (Exception ex)
        {
            RelationLines.Clear();
            RelationLines.Add($"Relation load failed: {ex.Message}");
        }
    }

    private void ResetDetails()
    {
        DetailTitle = "No node selected";
        DetailMeta = "-";
        DetailSummary = "Select a node in the graph.";
        EditHintText = "Select a module node to edit its definition.";
        KnowledgeHintText = "Select a real module to edit knowledge.";
        EditDiscipline = string.Empty;
        EditLayer = string.Empty;
        EditName = string.Empty;
        EditPath = string.Empty;
        SelectedParent = null;
        EditManagedPaths = string.Empty;
        EditMaintainer = string.Empty;
        EditSummary = string.Empty;
        EditBoundary = string.Empty;
        EditDependencies = string.Empty;
        EditPublicApi = string.Empty;
        EditConstraints = string.Empty;
        EditWorkflow = string.Empty;
        EditRules = string.Empty;
        EditProhibitions = string.Empty;
        EditMetadata = "{}";
        McpPreview = string.Empty;
        KnowledgeIdentity = string.Empty;
        KnowledgeLessons = string.Empty;
        KnowledgeActiveTasks = string.Empty;
        KnowledgeFacts = string.Empty;
        EditStatusText = "Module editor ready.";
        KnowledgeStatusText = "Knowledge editor ready.";
    }

    private string BuildMcpPreview()
    {
        return
            $"upsert_module(\n" +
            $"  discipline={JsonSerializer.Serialize(string.IsNullOrWhiteSpace(EditDiscipline) ? "engineering" : EditDiscipline)},\n" +
            $"  name={JsonSerializer.Serialize(EditName)},\n" +
            $"  path={JsonSerializer.Serialize(EditPath)},\n" +
            $"  layer={(int.TryParse(EditLayer, out var layer) ? layer : 0)},\n" +
            $"  parent={JsonSerializer.Serialize(SelectedParent?.NodeId)},\n" +
            $"  dependencies={JsonSerializer.Serialize(ParseMultilineList(EditDependencies))}\n" +
            $")";
    }

    private static string BuildDetailMeta(TopologyWorkbenchModuleView module)
    {
        var path = string.IsNullOrWhiteSpace(module.RelativePath) ? "-" : module.RelativePath;
        return $"{module.TypeLabel} | {module.DisciplineDisplayName} | L{module.Layer} | {path}";
    }

    private static IReadOnlyList<string> ParseMetadataList(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return Array.Empty<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(value);
            return parsed?.Where(static item => !string.IsNullOrWhiteSpace(item)).Select(static item => item.Trim()).ToArray()
                   ?? Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static Dictionary<string, string>? ParseMetadataJson(string? raw)
    {
        var text = string.IsNullOrWhiteSpace(raw) ? "{}" : raw.Trim();
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
        return parsed is { Count: > 0 }
            ? new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    private static void UpsertMetadataList(Dictionary<string, string>? metadata, string key, List<string> values)
    {
        if (metadata is null)
            return;

        if (values.Count == 0)
        {
            metadata.Remove(key);
            return;
        }

        metadata[key] = JsonSerializer.Serialize(values);
    }

    private static string ToMultilineText(IEnumerable<string>? values)
        => values is null ? string.Empty : string.Join(Environment.NewLine, values.Where(static value => !string.IsNullOrWhiteSpace(value)));

    private static List<string> ParseMultilineList(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<LessonSummary> ParseLessons(string? raw)
    {
        return ParseMultilineList(raw)
            .Select(static item => new LessonSummary
            {
                Title = item
            })
            .ToList();
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TopologyParentOptionViewModel(string? NodeId, string Label)
{
    public override string ToString() => Label;
}
