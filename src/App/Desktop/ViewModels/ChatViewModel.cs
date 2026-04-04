using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IDnaApiClient _apiClient;

    [ObservableProperty]
    private string _chatSubtitleText = "加载项目后可在这里直接与本地 Agent Shell 对话。";

    [ObservableProperty]
    private string _chatStatusText = "加载项目后可用。";

    [ObservableProperty]
    private string _chatInputBoxText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ChatProviderOption> _chatProviders = new();

    [ObservableProperty]
    private ChatProviderOption? _selectedChatProvider;

    [ObservableProperty]
    private ObservableCollection<ChatMessageViewModel> _chatMessages = new();

    [ObservableProperty]
    private ObservableCollection<ChatSessionViewModel> _chatSessions = new();

    [ObservableProperty]
    private bool _isViewingChatSessions;

    [ObservableProperty]
    private bool _isChatStreaming;

    [ObservableProperty]
    private string _chatMode = "agent";

    [ObservableProperty]
    private string _chatSessionId = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private string _activeChatProviderLabel = "未配置";

    [ObservableProperty]
    private bool _isChatReady;

    [ObservableProperty]
    private bool _isAskModeActive;

    [ObservableProperty]
    private bool _isPlanModeActive;

    [ObservableProperty]
    private bool _isAgentModeActive = true;

    private CancellationTokenSource? _chatStreamingCts;
    private string? _projectRoot;

    public bool IsChatMessagesVisible => !IsViewingChatSessions;
    public bool IsChatSessionsVisible => IsViewingChatSessions;

    public ChatViewModel(IDnaApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    partial void OnIsViewingChatSessionsChanged(bool value)
    {
        OnPropertyChanged(nameof(IsChatMessagesVisible));
        OnPropertyChanged(nameof(IsChatSessionsVisible));
    }

    public void Reset()
    {
        ChatSubtitleText = "加载项目后可在这里直接与本地 Agent Shell 对话。";
        ChatStatusText = "加载项目后可用。";
        ChatInputBoxText = string.Empty;
        ChatProviders.Clear();
        SelectedChatProvider = null;
        ChatMessages.Clear();
        ChatSessions.Clear();
        ActiveChatProviderLabel = "未配置";
        IsViewingChatSessions = false;
        SetChatMode("agent");
        IsChatReady = false;
    }

    public void ApplyProject(DesktopProjectConfig project)
    {
        _projectRoot = project.ProjectRoot;
        ChatMessages.Clear();
        ChatSessionId = Guid.NewGuid().ToString("N");
        IsViewingChatSessions = false;
        StopChatStreaming();
        ChatSubtitleText = $"当前项目：{project.ProjectName} · 本地 Agent Shell";
        RenderChatWelcome();
    }

    public void ApplyConnectionState(MainWindow.ConnectionAccessState access)
    {
        if (!access.HasProject)
        {
            IsChatReady = false;
            return;
        }

        if (!access.RuntimeOnline)
        {
            IsChatReady = false;
            return;
        }

        IsChatReady = true;
    }

    [RelayCommand]
    public async Task RefreshChatShellAsync(string? projectRoot)
    {
        _projectRoot = projectRoot;

        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            ChatSubtitleText = "加载项目后可在这里直接与本地 Agent Shell 对话。";
            ChatStatusText = "请先加载项目。";
            ChatProviders.Clear();
            SelectedChatProvider = null;
            ActiveChatProviderLabel = "未配置";
            RenderChatWelcome();
            IsChatReady = false;
            return;
        }

        if (!IsChatReady)
        {
            ChatStatusText = "本地 5052 运行时未连接，聊天暂不可用。";
            ChatProviders.Clear();
            SelectedChatProvider = null;
            ActiveChatProviderLabel = "未连接";
            UpdateChatSubtitle();
            return;
        }

        try
        {
            var providerState = await _apiClient.GetAsync("/agent/providers");
            var activeProviderId = GetString(providerState, "activeProviderId", null);
            
            ChatProviders.Clear();
            if (providerState.TryGetProperty("providers", out var providers) &&
                providers.ValueKind == JsonValueKind.Array)
            {
                foreach (var provider in providers.EnumerateArray())
                {
                    var id = GetString(provider, "id", string.Empty) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    var label = BuildChatProviderLabel(provider);
                    ChatProviders.Add(new ChatProviderOption(id, label));
                }
            }

            SelectedChatProvider = ChatProviders.FirstOrDefault(item =>
                string.Equals(item.Id, activeProviderId, StringComparison.OrdinalIgnoreCase))
                ?? ChatProviders.FirstOrDefault();

            ActiveChatProviderLabel = SelectedChatProvider?.Label ?? (ChatProviders.Count == 0 ? "未配置" : ChatProviders[0].Label);

            UpdateChatSubtitle();
            if (IsViewingChatSessions)
                await ShowChatSessionsAsync();
            else
            {
                if (ChatMessages.Count == 0) RenderChatWelcome();
            }

            if (!IsChatStreaming)
                ChatStatusText = ChatProviders.Count == 0 ? "当前没有可用模型。" : "本地 Agent Shell 已就绪。";
        }
        catch (Exception ex)
        {
            ChatStatusText = $"聊天面板刷新失败：{ex.Message}";
        }
    }

    partial void OnSelectedChatProviderChanged(ChatProviderOption? value)
    {
        if (value is null) return;

        Task.Run(async () =>
        {
            try
            {
                await _apiClient.PostAsync("/agent/providers/active", new { id = value.Id });
                ActiveChatProviderLabel = value.Label;
                ChatStatusText = $"已切换模型：{value.Label}";
                UpdateChatSubtitle();
            }
            catch (Exception ex)
            {
                ChatStatusText = $"切换模型失败：{ex.Message}";
            }
        });
    }

    [RelayCommand]
    public async Task ToggleChatSessionsAsync()
    {
        if (IsViewingChatSessions)
        {
            IsViewingChatSessions = false;
            return;
        }

        await SaveCurrentChatSessionAsync();
        await ShowChatSessionsAsync();
    }

    [RelayCommand]
    public async Task StartNewChatAsync()
    {
        await SaveCurrentChatSessionAsync();
        StopChatStreaming();

        ChatSessionId = Guid.NewGuid().ToString("N");
        ChatMessages.Clear();
        IsViewingChatSessions = false;
        RenderChatWelcome();
        ChatStatusText = "已创建新对话。";
    }

    [RelayCommand]
    public async Task SendChatMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(_projectRoot))
        {
            ChatStatusText = "请先加载项目后再发消息。";
            return;
        }

        if (!IsChatReady)
        {
            ChatStatusText = "本地运行时未就绪，暂时无法发送。";
            return;
        }

        var prompt = (ChatInputBoxText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ChatStatusText = "请输入要发送的内容。";
            return;
        }

        StopChatStreaming();
        IsViewingChatSessions = false;

        // Remove welcome message if present
        if (ChatMessages.Count == 1 && ChatMessages[0].IsWelcome)
        {
            ChatMessages.Clear();
        }

        ChatMessages.Add(new ChatMessageViewModel("user", prompt));
        ChatInputBoxText = string.Empty;
        ChatStatusText = $"正在以 {GetChatModeLabel(ChatMode)} 模式发送…";
        IsChatStreaming = true;

        _chatStreamingCts = new CancellationTokenSource();
        var token = _chatStreamingCts.Token;

        try
        {
            var content = new StringContent(JsonSerializer.Serialize(new
            {
                id = ChatSessionId,
                mode = ChatMode,
                prompt
            }), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "/agent/chat") { Content = content };
            using var response = await _apiClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream);
            var chunk = string.Empty;

            while (!reader.EndOfStream && !token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(token);
                if (line is null) break;

                if (line.StartsWith("data: "))
                {
                    var data = line["data: ".Length..];
                    if (data == "[DONE]") break;

                    chunk += data;
                    
                    var lastMsg = ChatMessages.LastOrDefault();
                    if (lastMsg != null && lastMsg.Role == "assistant")
                    {
                        lastMsg.Content = chunk;
                    }
                    else
                    {
                        ChatMessages.Add(new ChatMessageViewModel("assistant", chunk));
                    }
                }
            }

            ChatStatusText = "本轮对话已完成。";
            await SaveCurrentChatSessionAsync();
        }
        catch (OperationCanceledException)
        {
            ChatStatusText = "当前输出已停止。";
            await SaveCurrentChatSessionAsync();
        }
        catch (Exception ex)
        {
            ChatStatusText = $"发送失败：{ex.Message}";
        }
        finally
        {
            IsChatStreaming = false;
        }
    }

    [RelayCommand]
    public void StopChat()
    {
        StopChatStreaming();
        ChatStatusText = "已请求停止当前输出。";
    }

    [RelayCommand]
    public void SwitchChatMode(string mode)
    {
        SetChatMode(mode);
    }

    [RelayCommand]
    public async Task LoadChatSessionAsync(string sessionId)
    {
        try
        {
            var session = await _apiClient.GetAsync($"/agent/sessions/{Uri.EscapeDataString(sessionId)}");
            
            ChatMessages.Clear();
            if (session.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray())
                {
                    var role = NormalizeChatRole(GetString(item, "role", "assistant"));
                    var content = GetString(item, "content", string.Empty) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(content))
                        continue;

                    ChatMessages.Add(new ChatMessageViewModel(role, content));
                }
            }

            ChatSessionId = GetString(session, "id", sessionId) ?? sessionId;
            ChatMode = NormalizeChatMode(GetString(session, "mode", "agent"));
            
            IsViewingChatSessions = false;
            SetChatMode(ChatMode);
            ChatStatusText = $"已加载会话：{GetString(session, "title", "未命名会话")}";
        }
        catch (Exception ex)
        {
            ChatStatusText = $"加载会话失败：{ex.Message}";
        }
    }

    private async Task ShowChatSessionsAsync()
    {
        IsViewingChatSessions = true;
        ChatSessions.Clear();

        if (string.IsNullOrWhiteSpace(_projectRoot) || !IsChatReady)
        {
            return;
        }

        try
        {
            var data = await _apiClient.GetAsync("/agent/sessions");

            if (data.TryGetProperty("sessions", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    var id = GetString(item, "id", string.Empty) ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    ChatSessions.Add(new ChatSessionViewModel(
                        id,
                        GetString(item, "title", "未命名会话") ?? "未命名会话",
                        NormalizeChatMode(GetString(item, "mode", "agent")),
                        ParseDate(item, "updatedAt").ToLocalTime().ToString("MM-dd HH:mm"),
                        ParseNullableInt(item, "messageCount") ?? 0,
                        string.Equals(id, ChatSessionId, StringComparison.OrdinalIgnoreCase)));
                }
            }
        }
        catch (Exception ex)
        {
            ChatStatusText = $"加载会话失败：{ex.Message}";
        }
    }

    private async Task SaveCurrentChatSessionAsync()
    {
        if (ChatMessages.Count == 0 || (ChatMessages.Count == 1 && ChatMessages[0].IsWelcome))
            return;

        try
        {
            await _apiClient.PostAsync("/agent/sessions/save", new
            {
                id = ChatSessionId,
                mode = ChatMode,
                title = BuildChatSessionTitle(),
                messages = ChatMessages.Select(m => new { role = m.Role, content = m.Content }).ToList()
            });
        }
        catch
        {
            // Ignore save errors
        }
    }

    private void StopChatStreaming()
    {
        if (_chatStreamingCts is not null && !_chatStreamingCts.IsCancellationRequested)
        {
            _chatStreamingCts.Cancel();
            _chatStreamingCts.Dispose();
        }
        _chatStreamingCts = null;
        IsChatStreaming = false;
    }

    private void SetChatMode(string mode)
    {
        ChatMode = NormalizeChatMode(mode);
        IsAskModeActive = string.Equals(ChatMode, "ask", StringComparison.OrdinalIgnoreCase);
        IsPlanModeActive = string.Equals(ChatMode, "plan", StringComparison.OrdinalIgnoreCase);
        IsAgentModeActive = string.Equals(ChatMode, "agent", StringComparison.OrdinalIgnoreCase);
        UpdateChatSubtitle();
    }

    private void UpdateChatSubtitle()
    {
        var mode = GetChatModeLabel(ChatMode);
        var projectName = string.IsNullOrWhiteSpace(_projectRoot) ? "未加载" : Path.GetFileName(_projectRoot);
        ChatSubtitleText = $"当前项目：{projectName} · 模式：{mode} · 模型：{ActiveChatProviderLabel}";
    }

    private void RenderChatWelcome()
    {
        ChatMessages.Clear();
        ChatMessages.Add(new ChatMessageViewModel("assistant", "你好！我是 Agentic OS 本地 Agent。请问有什么我可以帮你的？\n\n你可以：\n- 询问关于当前项目架构的问题\n- 让我帮你规划重构方案\n- 直接让我执行代码修改或终端命令") { IsWelcome = true });
    }

    private string BuildChatSessionTitle()
    {
        var firstUser = ChatMessages.FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (firstUser is null || string.IsNullOrWhiteSpace(firstUser.Content))
            return "新会话";

        var text = firstUser.Content.Trim();
        var newline = text.IndexOf('\n');
        if (newline > 0)
            text = text[..newline];

        return text.Length > 20 ? text[..20] + "..." : text;
    }

    private static string BuildChatProviderLabel(JsonElement provider)
    {
        var label = GetString(provider, "label", null);
        if (!string.IsNullOrWhiteSpace(label))
            return label;

        var name = GetString(provider, "name", "Unknown");
        var model = GetString(provider, "model", "default");
        return $"{name} ({model})";
    }

    private static string NormalizeChatMode(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return m switch
        {
            "ask" => "ask",
            "plan" => "plan",
            "agent" => "agent",
            "chat" => "chat",
            _ => "agent"
        };
    }

    private static string NormalizeChatRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        return r switch
        {
            "user" => "user",
            "assistant" => "assistant",
            "system" => "system",
            _ => "assistant"
        };
    }

    private static string GetChatModeLabel(string mode)
    {
        return NormalizeChatMode(mode) switch
        {
            "ask" => "Ask",
            "plan" => "Plan",
            "agent" => "Agent",
            "chat" => "Chat",
            _ => "Agent"
        };
    }

    private static string? GetString(JsonElement element, string propertyName, string? fallback = null)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return fallback;

        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();

        return fallback;
    }

    private static DateTime ParseDate(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String && DateTime.TryParse(value.GetString(), out var date))
            return date;
        return DateTime.MinValue;
    }

    private static int? ParseNullableInt(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return null;
    }
}

public partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _role;

    [ObservableProperty]
    private string _content;

    public bool IsWelcome { get; set; }

    public ChatMessageViewModel(string role, string content)
    {
        Role = role;
        Content = content;
    }
}

public record ChatProviderOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public record ChatSessionViewModel(string Id, string Title, string Mode, string UpdatedAtLabel, int MessageCount, bool IsCurrent)
{
    public override string ToString() => $"{Title} [{Mode}] {UpdatedAtLabel}";
}
