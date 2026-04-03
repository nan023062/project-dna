using System.Net.Http;
using System.Text;
using System.Text.Json;
using Avalonia.Interactivity;

namespace Dna.App.Desktop;

public partial class MainWindow
{
    private string? _topologyKnowledgeNodeId;
    private TopologyKnowledgeEditorState _topologyKnowledgeBaseline = TopologyKnowledgeEditorState.Empty;
    private int _topologyDetailRequestVersion;

    private void BeginTopologyDetailLoad(TopologyNodeViewModel node)
    {
        var requestVersion = ++_topologyDetailRequestVersion;
        SetTopologyRelationLoadingState(node);
        _ = LoadTopologyNodeRelationsAsync(node, requestVersion);

        if (node.CanEdit)
        {
            SetTopologyKnowledgeLoadingState(node);
            _ = LoadTopologyNodeKnowledgeAsync(node, requestVersion);
            return;
        }

        ShowTopologyKnowledgeUnavailable(node);
    }

    private void CancelTopologyDetailRequests()
    {
        _topologyDetailRequestVersion++;
    }

    private async void SaveTopologyKnowledge_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) ||
            !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node) ||
            !node.CanEdit)
        {
            TopologyKnowledgeStatusText.Text = "请先选择一个真实模块节点。";
            return;
        }

        if (!_connectionAccess.IsAdmin)
        {
            TopologyKnowledgeStatusText.Text = "当前角色不是 admin，无法保存模块知识。";
            return;
        }

        if (_project is null)
        {
            TopologyKnowledgeStatusText.Text = "未选择项目，无法保存模块知识。";
            return;
        }

        try
        {
            var payload = new
            {
                identity = EmptyToNull(TopologyKnowledgeIdentityBox.Text),
                lessons = ParseTopologyLessonDrafts(TopologyKnowledgeLessonsBox.Text)
                    .Select(item => new
                    {
                        title = item.Title,
                        resolution = item.Resolution,
                        severity = item.Severity
                    })
                    .ToList(),
                activeTasks = ParseMultilineList(TopologyKnowledgeActiveTasksBox.Text),
                facts = ParseMultilineList(TopologyKnowledgeFactsBox.Text),
                totalMemoryCount = 0,
                identityMemoryId = (string?)null,
                upgradeTrailMemoryId = (string?)null,
                memoryIds = Array.Empty<string>()
            };

            var result = await PutJsonAsync(BuildLocalUrl($"/api/modules/{Uri.EscapeDataString(node.NodeId)}/knowledge"), payload);
            var state = ParseTopologyKnowledgeState(result, node.NodeId, node.Label);
            _topologyKnowledgeNodeId = state.NodeId;
            _topologyKnowledgeBaseline = state;
            ApplyTopologyKnowledgeState(state);
            SetTopologyKnowledgeEditorEnabled(true, _connectionAccess.IsAdmin);
            TopologyKnowledgeHintText.Text = _connectionAccess.IsAdmin
                ? "当前模块知识已保存，可继续编辑。"
                : "当前模块知识已保存，但当前运行态为只读。";
            TopologyKnowledgeParallelHintText.Text = "手动保存与 MCP/CLI 写入共用同一条知识存储链路。";
            TopologyKnowledgeStatusText.Text = $"模块知识已保存：{node.Label}";
        }
        catch (Exception ex)
        {
            TopologyKnowledgeStatusText.Text = $"保存模块知识失败：{ex.Message}";
        }
    }

    private async void RefreshTopologyKnowledge_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) ||
            !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node))
        {
            ResetTopologyKnowledgeEditor();
            TopologyKnowledgeStatusText.Text = "没有可读取的模块知识。";
            return;
        }

        if (!node.CanEdit)
        {
            ShowTopologyKnowledgeUnavailable(node);
            return;
        }

        var requestVersion = ++_topologyDetailRequestVersion;
        SetTopologyKnowledgeLoadingState(node);
        _ = LoadTopologyNodeKnowledgeAsync(node, requestVersion);
    }

    private void ResetTopologyKnowledgeEditor_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedTopologyNodeId) ||
            !_topologyNodes.TryGetValue(_selectedTopologyNodeId, out var node) ||
            !node.CanEdit)
        {
            ResetTopologyKnowledgeEditor();
            return;
        }

        ApplyTopologyKnowledgeState(_topologyKnowledgeBaseline);
        SetTopologyKnowledgeEditorEnabled(true, _connectionAccess.IsAdmin);
        TopologyKnowledgeStatusText.Text = _topologyKnowledgeBaseline == TopologyKnowledgeEditorState.Empty
            ? $"模块 {node.Label} 还没有已加载知识，表单已清空。"
            : $"已恢复到最近一次加载的模块知识：{node.Label}";
    }

    private void SetTopologyRelationLoadingState(TopologyNodeViewModel node)
    {
        TopologyRelationListBox.ItemsSource = new[]
        {
            $"正在加载完整关系：{node.Label}"
        };
    }

    private void SetTopologyKnowledgeLoadingState(TopologyNodeViewModel node)
    {
        _topologyKnowledgeNodeId = node.NodeId;
        _topologyKnowledgeBaseline = TopologyKnowledgeEditorState.Empty;
        ApplyTopologyKnowledgeState(TopologyKnowledgeEditorState.Create(node.NodeId, node.Label, string.Empty, string.Empty, string.Empty, string.Empty));
        SetTopologyKnowledgeEditorEnabled(true, false);
        TopologyKnowledgeHintText.Text = _connectionAccess.IsAdmin
            ? "正在读取当前模块知识，加载完成后可直接编辑。"
            : "正在读取当前模块知识，当前运行态为只读。";
        TopologyKnowledgeParallelHintText.Text = "手动保存与 MCP/CLI 写入共用同一条知识存储链路。";
        TopologyKnowledgeStatusText.Text = $"正在加载模块知识：{node.Label}";
    }

    private void ShowTopologyKnowledgeUnavailable(TopologyNodeViewModel node)
    {
        _topologyKnowledgeNodeId = null;
        _topologyKnowledgeBaseline = TopologyKnowledgeEditorState.Empty;
        ApplyTopologyKnowledgeState(TopologyKnowledgeEditorState.Empty);
        SetTopologyKnowledgeEditorEnabled(false, false);
        TopologyKnowledgeHintText.Text = "当前节点是分组节点，模块知识编辑仅对真实模块开放。";
        TopologyKnowledgeParallelHintText.Text = "Project / Department 节点保留拓扑浏览职责，不写入模块知识。";
        TopologyKnowledgeStatusText.Text = $"当前节点 {node.Label} 暂无模块知识编辑。";
    }

    private void ResetTopologyKnowledgeEditor()
    {
        _topologyKnowledgeNodeId = null;
        _topologyKnowledgeBaseline = TopologyKnowledgeEditorState.Empty;
        ApplyTopologyKnowledgeState(TopologyKnowledgeEditorState.Empty);
        SetTopologyKnowledgeEditorEnabled(false, false);
        TopologyKnowledgeHintText.Text = "选择真实模块后，可查看或编辑当前模块的 identity、lessons、active tasks 与 facts。";
        TopologyKnowledgeParallelHintText.Text = "手动保存与 MCP/CLI 写入共用同一条知识存储链路。";
        TopologyKnowledgeStatusText.Text = "模块知识区就绪。";
    }

    private void ApplyTopologyKnowledgeState(TopologyKnowledgeEditorState state)
    {
        TopologyKnowledgeIdentityBox.Text = state.Identity;
        TopologyKnowledgeLessonsBox.Text = state.Lessons;
        TopologyKnowledgeActiveTasksBox.Text = state.ActiveTasks;
        TopologyKnowledgeFactsBox.Text = state.Facts;
    }

    private void SetTopologyKnowledgeEditorEnabled(bool hasModule, bool canSave)
    {
        var readOnly = hasModule && !canSave;

        TopologyKnowledgeIdentityBox.IsEnabled = hasModule;
        TopologyKnowledgeIdentityBox.IsReadOnly = readOnly;
        TopologyKnowledgeLessonsBox.IsEnabled = hasModule;
        TopologyKnowledgeLessonsBox.IsReadOnly = readOnly;
        TopologyKnowledgeActiveTasksBox.IsEnabled = hasModule;
        TopologyKnowledgeActiveTasksBox.IsReadOnly = readOnly;
        TopologyKnowledgeFactsBox.IsEnabled = hasModule;
        TopologyKnowledgeFactsBox.IsReadOnly = readOnly;

        TopologySaveKnowledgeButton.IsEnabled = hasModule && canSave;
        TopologyReloadKnowledgeButton.IsEnabled = hasModule;
        TopologyResetKnowledgeButton.IsEnabled = hasModule;
    }

    private bool IsCurrentTopologyDetailRequest(string nodeId, int requestVersion)
    {
        return requestVersion == _topologyDetailRequestVersion &&
               string.Equals(_selectedTopologyNodeId, nodeId, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadTopologyNodeKnowledgeAsync(TopologyNodeViewModel node, int requestVersion)
    {
        try
        {
            var result = await GetJsonAsync(BuildLocalUrl($"/api/modules/{Uri.EscapeDataString(node.NodeId)}/knowledge"));
            if (!IsCurrentTopologyDetailRequest(node.Id, requestVersion))
                return;

            var state = ParseTopologyKnowledgeState(result, node.NodeId, node.Label);
            _topologyKnowledgeNodeId = state.NodeId;
            _topologyKnowledgeBaseline = state;
            ApplyTopologyKnowledgeState(state);
            SetTopologyKnowledgeEditorEnabled(true, _connectionAccess.IsAdmin);
            TopologyKnowledgeHintText.Text = _connectionAccess.IsAdmin
                ? "当前模块知识可直接编辑并保存到文件协议。"
                : "当前运行态只读，仅展示模块知识。";
            TopologyKnowledgeParallelHintText.Text = "手动保存与 MCP/CLI 写入共用同一条知识存储链路。";
            TopologyKnowledgeStatusText.Text = $"已加载模块知识：{node.Label}";
        }
        catch (Exception ex)
        {
            if (!IsCurrentTopologyDetailRequest(node.Id, requestVersion))
                return;

            ApplyTopologyKnowledgeState(TopologyKnowledgeEditorState.Create(node.NodeId, node.Label, string.Empty, string.Empty, string.Empty, string.Empty));
            SetTopologyKnowledgeEditorEnabled(true, _connectionAccess.IsAdmin);
            TopologyKnowledgeHintText.Text = "读取模块知识失败，可重试或直接录入新的模块知识。";
            TopologyKnowledgeParallelHintText.Text = "手动保存与 MCP/CLI 写入共用同一条知识存储链路。";
            TopologyKnowledgeStatusText.Text = $"加载模块知识失败：{ex.Message}";
        }
    }

    private async Task LoadTopologyNodeRelationsAsync(TopologyNodeViewModel node, int requestVersion)
    {
        try
        {
            var result = await GetJsonAsync(BuildLocalUrl($"/api/modules/{Uri.EscapeDataString(node.NodeId)}/relations"));
            if (!IsCurrentTopologyDetailRequest(node.Id, requestVersion))
                return;

            TopologyRelationListBox.ItemsSource = BuildTopologyRelationLines(result);
        }
        catch (Exception ex)
        {
            if (!IsCurrentTopologyDetailRequest(node.Id, requestVersion))
                return;

            TopologyRelationListBox.ItemsSource = new[]
            {
                $"完整关系加载失败：{ex.Message}"
            };
        }
    }

    private async Task<JsonElement> PutJsonAsync(string absoluteUrl, object payload)
    {
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await ApiHttp.PutAsync(absoluteUrl, content);
        return await ReadJsonAsync(response, absoluteUrl);
    }

    private static IReadOnlyList<string> BuildTopologyRelationLines(JsonElement relations)
    {
        var outgoing = new List<(string TypeLabel, string PeerName, bool IsComputed)>();
        var incoming = new List<(string TypeLabel, string PeerName, bool IsComputed)>();

        if (relations.TryGetProperty("outgoing", out var outgoingElement) && outgoingElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var relation in outgoingElement.EnumerateArray())
            {
                outgoing.Add((
                    ResolveTopologyRelationTypeLabel(GetString(relation, "type", null), GetString(relation, "label", null)),
                    GetString(relation, "toName", GetString(relation, "toId", "-")) ?? "-",
                    relation.TryGetProperty("isComputed", out var computed) && computed.ValueKind == JsonValueKind.True));
            }
        }

        if (relations.TryGetProperty("incoming", out var incomingElement) && incomingElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var relation in incomingElement.EnumerateArray())
            {
                incoming.Add((
                    ResolveTopologyRelationTypeLabel(GetString(relation, "type", null), GetString(relation, "label", null)),
                    GetString(relation, "fromName", GetString(relation, "fromId", "-")) ?? "-",
                    relation.TryGetProperty("isComputed", out var computed) && computed.ValueKind == JsonValueKind.True));
            }
        }

        var lines = new List<string>
        {
            $"完整关系：出边 {outgoing.Count} | 入边 {incoming.Count}"
        };

        if (outgoing.Count == 0 && incoming.Count == 0)
        {
            lines.Add("当前模块暂无关系。");
            return lines;
        }

        if (outgoing.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(outgoing
                .OrderBy(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PeerName, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"OUT [{item.TypeLabel}{(item.IsComputed ? "/computed" : string.Empty)}] -> {item.PeerName}"));
        }

        if (incoming.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(incoming
                .OrderBy(item => item.TypeLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.PeerName, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"IN  [{item.TypeLabel}{(item.IsComputed ? "/computed" : string.Empty)}] <- {item.PeerName}"));
        }

        return lines;
    }

    private static string ResolveTopologyRelationTypeLabel(string? rawType, string? rawLabel)
    {
        if (!string.IsNullOrWhiteSpace(rawLabel))
            return rawLabel.Trim();

        return (rawType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dependency" => "依赖",
            "containment" => "包含",
            "collaboration" => "协作",
            _ => string.IsNullOrWhiteSpace(rawType) ? "关系" : rawType.Trim()
        };
    }

    private static TopologyKnowledgeEditorState ParseTopologyKnowledgeState(JsonElement element, string fallbackNodeId, string fallbackName)
    {
        var nodeId = GetString(element, "nodeId", fallbackNodeId) ?? fallbackNodeId;
        var name = GetString(element, "name", fallbackName) ?? fallbackName;
        if (!element.TryGetProperty("knowledge", out var knowledge) || knowledge.ValueKind != JsonValueKind.Object)
            return TopologyKnowledgeEditorState.Create(nodeId, name, string.Empty, string.Empty, string.Empty, string.Empty);

        return TopologyKnowledgeEditorState.Create(
            nodeId,
            name,
            GetString(knowledge, "identity", string.Empty) ?? string.Empty,
            BuildTopologyLessonText(knowledge),
            ToMultilineText(ParseStringArray(knowledge, "activeTasks")),
            ToMultilineText(ParseStringArray(knowledge, "facts")));
    }

    private static string BuildTopologyLessonText(JsonElement knowledge)
    {
        if (!knowledge.TryGetProperty("lessons", out var lessons) || lessons.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var lines = new List<string>();
        foreach (var lesson in lessons.EnumerateArray())
        {
            var title = EmptyToNull(GetString(lesson, "title", null));
            var resolution = EmptyToNull(GetString(lesson, "resolution", null));
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(resolution))
                continue;

            lines.Add(string.IsNullOrWhiteSpace(resolution)
                ? title ?? string.Empty
                : string.IsNullOrWhiteSpace(title)
                    ? resolution!
                    : $"{title} | {resolution}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<TopologyLessonDraft> ParseTopologyLessonDrafts(string? raw)
    {
        return (raw ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
                var title = EmptyToNull(parts[0]);
                if (string.IsNullOrWhiteSpace(title))
                    return null;

                var resolution = parts.Length > 1 ? EmptyToNull(parts[1]) : null;
                return new TopologyLessonDraft(title, resolution, null);
            })
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private sealed record TopologyKnowledgeEditorState(
        string NodeId,
        string Name,
        string Identity,
        string Lessons,
        string ActiveTasks,
        string Facts)
    {
        public static TopologyKnowledgeEditorState Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        public static TopologyKnowledgeEditorState Create(
            string nodeId,
            string name,
            string? identity,
            string? lessons,
            string? activeTasks,
            string? facts)
            => new(
                nodeId,
                name,
                identity ?? string.Empty,
                lessons ?? string.Empty,
                activeTasks ?? string.Empty,
                facts ?? string.Empty);
    }

    private sealed record TopologyLessonDraft(string Title, string? Resolution, string? Severity);
}
