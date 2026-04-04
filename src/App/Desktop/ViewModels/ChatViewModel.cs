using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dna.App.Desktop.Services;

namespace Dna.App.Desktop.ViewModels;

public partial class ChatViewModel : ObservableObject
{
    private readonly IDesktopLocalAgentClient _localAgentClient;

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

    public ChatViewModel(IDesktopLocalAgentClient localAgentClient)
    {
        _localAgentClient = localAgentClient;
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
        IsChatReady = access.HasProject && access.RuntimeOnline;
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
            ChatStatusText = "本地运行时未连接，聊天暂不可用。";
            ChatProviders.Clear();
            SelectedChatProvider = null;
            ActiveChatProviderLabel = "未连接";
            UpdateChatSubtitle();
            return;
        }

        try
        {
            var providerState = await _localAgentClient.GetChatProviderStateAsync();

            ChatProviders.Clear();
            foreach (var provider in providerState.Providers.Where(static provider => provider.Enabled))
                ChatProviders.Add(new ChatProviderOption(provider.Id, provider.Label));

            SelectedChatProvider = ChatProviders.FirstOrDefault(item =>
                string.Equals(item.Id, providerState.ActiveProviderId, StringComparison.OrdinalIgnoreCase))
                ?? ChatProviders.FirstOrDefault();

            ActiveChatProviderLabel = SelectedChatProvider?.Label ?? "未配置";
            UpdateChatSubtitle();

            if (IsViewingChatSessions)
                await ShowChatSessionsAsync();
            else if (ChatMessages.Count == 0)
                RenderChatWelcome();

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
        if (value is null)
            return;

        Task.Run(async () =>
        {
            try
            {
                var provider = await _localAgentClient.SetActiveChatProviderAsync(value.Id);
                ActiveChatProviderLabel = provider?.Label ?? value.Label;
                ChatStatusText = $"已切换模型：{ActiveChatProviderLabel}";
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

        if (ChatMessages.Count == 1 && ChatMessages[0].IsWelcome)
            ChatMessages.Clear();

        ChatMessages.Add(new ChatMessageViewModel("user", prompt));
        ChatInputBoxText = string.Empty;
        ChatStatusText = $"正在以 {GetChatModeLabel(ChatMode)} 模式发送...";
        IsChatStreaming = true;

        _chatStreamingCts = new CancellationTokenSource();
        var token = _chatStreamingCts.Token;

        try
        {
            var result = await _localAgentClient.SendChatAsync(new DesktopLocalChatSendRequest(
                SessionId: ChatSessionId,
                Mode: ChatMode,
                Prompt: prompt,
                ProjectRoot: _projectRoot,
                ProviderId: SelectedChatProvider?.Id), token);

            ChatSessionId = result.SessionId;
            ActiveChatProviderLabel = string.IsNullOrWhiteSpace(result.ActiveProviderLabel)
                ? ActiveChatProviderLabel
                : result.ActiveProviderLabel;

            ChatMessages.Add(new ChatMessageViewModel("assistant", result.AssistantMessage));
            ChatStatusText = "本轮对话已完成。";
            await SaveCurrentChatSessionAsync();
            UpdateChatSubtitle();
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
            var session = await _localAgentClient.GetChatSessionAsync(sessionId);
            if (session is null)
            {
                ChatStatusText = $"未找到会话：{sessionId}";
                return;
            }

            ChatMessages.Clear();
            foreach (var item in session.Messages)
            {
                if (string.IsNullOrWhiteSpace(item.Content))
                    continue;

                ChatMessages.Add(new ChatMessageViewModel(NormalizeChatRole(item.Role), item.Content));
            }

            ChatSessionId = session.Id;
            IsViewingChatSessions = false;
            SetChatMode(session.Mode);
            ChatStatusText = $"已加载会话：{session.Title}";
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
            return;

        try
        {
            var sessions = await _localAgentClient.ListChatSessionsAsync();
            foreach (var item in sessions)
            {
                ChatSessions.Add(new ChatSessionViewModel(
                    item.Id,
                    item.Title,
                    NormalizeChatMode(item.Mode),
                    item.UpdatedAtUtc.ToLocalTime().ToString("MM-dd HH:mm"),
                    item.MessageCount,
                    string.Equals(item.Id, ChatSessionId, StringComparison.OrdinalIgnoreCase)));
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
            var messages = ChatMessages
                .Where(message => !message.IsWelcome)
                .Select(message => new DesktopLocalChatMessage(
                    message.Role,
                    message.Content,
                    DateTime.UtcNow))
                .ToList();

            await _localAgentClient.SaveChatSessionAsync(new DesktopLocalChatSession(
                Id: ChatSessionId,
                Title: BuildChatSessionTitle(),
                Mode: ChatMode,
                CreatedAtUtc: DateTime.UtcNow,
                UpdatedAtUtc: DateTime.UtcNow,
                Messages: messages));
        }
        catch
        {
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
        ChatMessages.Add(new ChatMessageViewModel(
            "assistant",
            "你好！我是 Agentic OS 本地 Agent。\n\n你可以：\n- 询问当前项目结构\n- 让它先做计划\n- 让它基于本地知识和记忆给出下一步建议")
        { IsWelcome = true });
    }

    private string BuildChatSessionTitle()
    {
        var firstUser = ChatMessages.FirstOrDefault(message =>
            string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (firstUser is null || string.IsNullOrWhiteSpace(firstUser.Content))
            return "新会话";

        var text = firstUser.Content.Trim();
        var newline = text.IndexOf('\n');
        if (newline > 0)
            text = text[..newline];

        return text.Length > 20 ? text[..20] + "..." : text;
    }

    private static string NormalizeChatMode(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
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
        var value = (role ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
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
