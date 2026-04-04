using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class TopologyViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;
    private readonly Dictionary<string, JsonElement> _modulesById = new(StringComparer.OrdinalIgnoreCase);
    private string? _projectRoot;
    private string _scopeRootLabel = "root";

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

    public TopologyViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
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
            var snapshot = await _apiClient.GetAsync("/api/topology");
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
            EditStatusText = "请先选择一个模块。";
            return;
        }

        try
        {
            var metadata = ParseMetadataJson(EditMetadata)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UpsertMetadataList(metadata, "workflow", ParseMultilineList(EditWorkflow));
            UpsertMetadataList(metadata, "rules", ParseMultilineList(EditRules));
            UpsertMetadataList(metadata, "prohibitions", ParseMultilineList(EditProhibitions));

            var payload = new
            {
                discipline = string.IsNullOrWhiteSpace(EditDiscipline) ? "engineering" : EditDiscipline.Trim(),
                module = new
                {
                    id = SelectedNodeId,
                    name = EditName.Trim(),
                    path = EditPath.Trim(),
                    layer = int.TryParse(EditLayer, out var layer) ? layer : 0,
                    parentModuleId = SelectedParent?.NodeId,
                    managedPaths = ParseMultilineList(EditManagedPaths),
                    dependencies = ParseMultilineList(EditDependencies),
                    maintainer = EmptyToNull(EditMaintainer),
                    summary = EmptyToNull(EditSummary),
                    boundary = EmptyToNull(EditBoundary),
                    publicApi = ParseMultilineList(EditPublicApi),
                    constraints = ParseMultilineList(EditConstraints),
                    metadata
                }
            };

            await _apiClient.PostAsync("/api/modules", payload);
            EditStatusText = $"模块已保存：{EditName}";
            await RefreshAsync(_projectRoot);
            await SelectNodeAsync(SelectedNodeId);
        }
        catch (Exception ex)
        {
            EditStatusText = $"模块保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReloadModuleAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
            return;

        await SelectNodeAsync(SelectedNodeId);
        EditStatusText = $"模块已重新加载：{EditName}";
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
            KnowledgeStatusText = "请先选择一个模块。";
            return;
        }

        try
        {
            var payload = new
            {
                identity = EmptyToNull(KnowledgeIdentity),
                lessons = ParseLessons(KnowledgeLessons),
                activeTasks = ParseMultilineList(KnowledgeActiveTasks),
                facts = ParseMultilineList(KnowledgeFacts),
                totalMemoryCount = 0,
                identityMemoryId = (string?)null,
                upgradeTrailMemoryId = (string?)null,
                memoryIds = Array.Empty<string>()
            };

            await _apiClient.PutAsync($"/api/modules/{Uri.EscapeDataString(SelectedNodeId)}/knowledge", payload);
            KnowledgeStatusText = $"模块知识已保存：{EditName}";
            await SelectNodeAsync(SelectedNodeId);
        }
        catch (Exception ex)
        {
            KnowledgeStatusText = $"模块知识保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ReloadKnowledgeAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedNodeId))
            return;

        await LoadKnowledgeAsync(SelectedNodeId);
        KnowledgeStatusText = $"模块知识已重新加载：{EditName}";
    }

    [RelayCommand]
    public void ResetKnowledge()
    {
        KnowledgeIdentity = string.Empty;
        KnowledgeLessons = string.Empty;
        KnowledgeActiveTasks = string.Empty;
        KnowledgeFacts = string.Empty;
        KnowledgeStatusText = "知识表单已重置。";
    }

    public async Task SelectNodeAsync(string? nodeId)
    {
        SelectedNodeId = nodeId;
        if (string.IsNullOrWhiteSpace(nodeId) || !_modulesById.TryGetValue(nodeId, out var module))
        {
            ResetDetails();
            return;
        }

        var label = GetString(module, "displayName", GetString(module, "name", nodeId)) ?? nodeId;
        DetailTitle = label;
        DetailMeta = BuildDetailMeta(module);
        DetailSummary = GetString(module, "summary", "No summary.") ?? "No summary.";

        EditDiscipline = GetString(module, "discipline", string.Empty) ?? string.Empty;
        EditLayer = GetInt(module, "layer")?.ToString() ?? "0";
        EditName = GetString(module, "name", string.Empty) ?? string.Empty;
        EditPath = GetString(module, "relativePath", string.Empty) ?? string.Empty;
        EditManagedPaths = ToMultilineText(ParseStringArray(module, "managedPathScopes"));
        EditMaintainer = GetString(module, "maintainer", string.Empty) ?? string.Empty;
        EditSummary = GetString(module, "summary", string.Empty) ?? string.Empty;
        EditBoundary = GetString(module, "boundary", string.Empty) ?? string.Empty;
        EditDependencies = ToMultilineText(ParseStringArray(module, "dependencies"));
        EditPublicApi = ToMultilineText(ParseStringArray(module, "publicApi"));
        EditConstraints = ToMultilineText(ParseStringArray(module, "constraints"));
        EditWorkflow = ToMultilineText(ParseMetadataList(module, "workflow"));
        EditRules = ToMultilineText(ParseMetadataList(module, "rules"));
        EditProhibitions = ToMultilineText(ParseMetadataList(module, "prohibitions"));
        EditMetadata = JsonSerializer.Serialize(ParseStringDictionary(module, "metadata"), new JsonSerializerOptions { WriteIndented = true });
        McpPreview = BuildMcpPreview();
        EditHintText = $"正在编辑模块：{label}";

        var parentId = GetString(module, "parentModuleId", null);
        SelectedParent = ParentOptions.FirstOrDefault(item =>
            string.Equals(item.NodeId, parentId, StringComparison.OrdinalIgnoreCase));

        VisibleNodeLines.Clear();
        foreach (var item in GraphNodes.Select(node => $"{node.Label} | {node.TypeLabel} | {node.DisciplineLabel}"))
            VisibleNodeLines.Add(item);

        await LoadKnowledgeAsync(nodeId);
        await LoadRelationsAsync(nodeId);
    }

    public void ApplyScope(string? viewRootId, IReadOnlyList<string> scopeTrailIds, IReadOnlyList<string> visibleNodeIds, IReadOnlyList<TopologyEdgeViewModel> visibleEdges)
    {
        _scopeRootLabel = string.IsNullOrWhiteSpace(viewRootId)
            ? "root"
            : GraphNodes.FirstOrDefault(node => string.Equals(node.Id, viewRootId, StringComparison.OrdinalIgnoreCase))?.Label ?? viewRootId;
        TopologyScopeText = $"Current scope: {_scopeRootLabel}";

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

    private void BuildTopologyState(JsonElement snapshot)
    {
        _modulesById.Clear();
        GraphNodes.Clear();
        GraphEdges.Clear();
        DisciplineOptions.Clear();
        ParentOptions.Clear();

        var modules = snapshot.TryGetProperty("modules", out var moduleArray) && moduleArray.ValueKind == JsonValueKind.Array
            ? moduleArray.EnumerateArray().ToList()
            : [];
        var edges = snapshot.TryGetProperty("relationEdges", out var edgeArray) && edgeArray.ValueKind == JsonValueKind.Array
            ? edgeArray.EnumerateArray().ToList()
            : [];

        foreach (var module in modules)
        {
            var nodeId = GetString(module, "nodeId", string.Empty) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nodeId))
                continue;

            _modulesById[nodeId] = module.Clone();
            GraphNodes.Add(new TopologyNodeViewModel(
                Id: nodeId,
                NodeId: nodeId,
                Label: GetString(module, "displayName", GetString(module, "name", nodeId)) ?? nodeId,
                Type: GetString(module, "type", "Technical") ?? "Technical",
                TypeLabel: GetString(module, "typeLabel", GetString(module, "typeName", "Technical")) ?? "Technical",
                Discipline: GetString(module, "discipline", "root") ?? "root",
                DisciplineLabel: GetString(module, "disciplineDisplayName", GetString(module, "discipline", "root")) ?? "root",
                DependencyCount: ParseStringArray(module, "dependencies").Count,
                Summary: GetString(module, "summary", string.Empty) ?? string.Empty,
                ComputedLayer: GetInt(module, "architectureLayerScore"),
                RelativePath: GetString(module, "relativePath", null),
                ParentModuleId: GetString(module, "parentModuleId", null),
                ChildModuleIds: ParseStringArray(module, "childIds"),
                ManagedPathScopes: ParseStringArray(module, "managedPathScopes"),
                Maintainer: GetString(module, "maintainer", null),
                Boundary: GetString(module, "boundary", null),
                PublicApi: ParseStringArray(module, "publicApi"),
                Constraints: ParseStringArray(module, "constraints"),
                Workflow: ParseMetadataList(module, "workflow"),
                Rules: ParseMetadataList(module, "rules"),
                Prohibitions: ParseMetadataList(module, "prohibitions"),
                Metadata: ParseStringDictionary(module, "metadata"),
                CanEdit: true));
        }

        foreach (var edge in edges)
        {
            GraphEdges.Add(new TopologyEdgeViewModel(
                From: GetString(edge, "from", string.Empty) ?? string.Empty,
                To: GetString(edge, "to", string.Empty) ?? string.Empty,
                Relation: GetString(edge, "relation", "dependency") ?? "dependency",
                IsComputed: GetBool(edge, "isComputed"),
                Kind: GetString(edge, "kind", null)));
        }

        foreach (var discipline in GraphNodes.Select(node => node.Discipline).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x))
            DisciplineOptions.Add(discipline);

        ParentOptions.Add(new TopologyParentOptionViewModel(null, "(none)"));
        foreach (var node in GraphNodes.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase))
            ParentOptions.Add(new TopologyParentOptionViewModel(node.NodeId, node.Label));

        TopologySummaryText = GetString(snapshot, "summary", $"Loaded {GraphNodes.Count} modules.") ?? $"Loaded {GraphNodes.Count} modules.";
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
            var result = await _apiClient.GetAsync($"/api/modules/{Uri.EscapeDataString(nodeId)}/knowledge");
            KnowledgeIdentity = GetString(result, "identity", string.Empty) ?? string.Empty;
            KnowledgeLessons = string.Join(Environment.NewLine, ParseLessonsToLines(result));
            KnowledgeActiveTasks = ToMultilineText(ParseStringArray(result, "activeTasks"));
            KnowledgeFacts = ToMultilineText(ParseStringArray(result, "facts"));
            KnowledgeHintText = $"正在编辑模块知识：{GetString(result, "name", EditName) ?? EditName}";
            KnowledgeStatusText = "Knowledge editor ready.";
        }
        catch (Exception ex)
        {
            KnowledgeStatusText = $"知识读取失败：{ex.Message}";
        }
    }

    private async Task LoadRelationsAsync(string nodeId)
    {
        try
        {
            var result = await _apiClient.GetAsync($"/api/modules/{Uri.EscapeDataString(nodeId)}/relations");
            RelationLines.Clear();

            if (result.TryGetProperty("outgoing", out var outgoing) && outgoing.ValueKind == JsonValueKind.Array)
            {
                foreach (var relation in outgoing.EnumerateArray())
                {
                    RelationLines.Add(
                        $"{GetString(relation, "type", "-")}: {GetString(relation, "toName", GetString(relation, "toId", "-"))}");
                }
            }

            if (result.TryGetProperty("incoming", out var incoming) && incoming.ValueKind == JsonValueKind.Array)
            {
                foreach (var relation in incoming.EnumerateArray())
                {
                    RelationLines.Add(
                        $"{GetString(relation, "type", "-")}: {GetString(relation, "fromName", GetString(relation, "fromId", "-"))}");
                }
            }
        }
        catch (Exception ex)
        {
            RelationLines.Clear();
            RelationLines.Add($"关系读取失败：{ex.Message}");
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

    private static string BuildDetailMeta(JsonElement module)
    {
        var type = GetString(module, "typeLabel", GetString(module, "typeName", "Technical")) ?? "Technical";
        var discipline = GetString(module, "disciplineDisplayName", GetString(module, "discipline", "root")) ?? "root";
        var layer = GetInt(module, "layer")?.ToString() ?? "0";
        var path = GetString(module, "relativePath", "-") ?? "-";
        return $"{type} | {discipline} | L{layer} | {path}";
    }

    private static string? GetString(JsonElement element, string propertyName, string? fallback = null)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
            return parsed;

        return null;
    }

    private static bool GetBool(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> ParseStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return value.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ParseStringDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            var raw = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : property.Value.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
                result[property.Name] = raw.Trim();
        }

        return result;
    }

    private static IReadOnlyList<string> ParseMetadataList(JsonElement element, string key)
    {
        if (!element.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
            return Array.Empty<string>();
        if (!metadata.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
            return Array.Empty<string>();

        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(value.GetString() ?? "[]");
            return parsed?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() ?? Array.Empty<string>();
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
        => values is null ? string.Empty : string.Join(Environment.NewLine, values.Where(x => !string.IsNullOrWhiteSpace(x)));

    private static List<string> ParseMultilineList(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<object> ParseLessons(string? raw)
    {
        return ParseMultilineList(raw)
            .Select(item => (object)new { title = item, severity = (string?)null, resolution = (string?)null })
            .ToList();
    }

    private static IReadOnlyList<string> ParseLessonsToLines(JsonElement element)
    {
        if (!element.TryGetProperty("lessons", out var lessons) || lessons.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return lessons.EnumerateArray()
            .Select(item => GetString(item, "title", null))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!.Trim())
            .ToArray();
    }

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TopologyParentOptionViewModel(string? NodeId, string Label)
{
    public override string ToString() => Label;
}
