using Microsoft.Maui.Dispatching;
using System.Collections.ObjectModel;
using System.Windows.Input;
using ZCL.API;
using ZCL.Models;
using ZCL.Protocol.ZCSP;
using ZCL.Services.Messaging;
using ZCM.Notifications;

namespace ZCM.ViewModels;

public sealed class MessagingViewModel : BindableObject
{
    private readonly ZcspPeer _peer;
    private readonly MessagingService _messaging;
    private readonly IChatQueryService _chatQueries;
    private readonly DataStore _store;

    public event Action? MessagesChanged;

    private Guid? _localPeerDbId;
    private ConversationItem? _activeConversation;
    private string? _activeProtocolPeerId;
    private bool _sessionReady;

    public ObservableCollection<ConversationItem> Conversations { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnPropertyChanged(); }
    }

    private string _statusMessage = "Idle";
    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    private string _outgoingMessage = string.Empty;
    public string OutgoingMessage
    {
        get => _outgoingMessage;
        set { _outgoingMessage = value; OnPropertyChanged(); }
    }

    public ICommand SendMessageCommand { get; }

    public MessagingViewModel(
        ZcspPeer peer,
        MessagingService messaging,
        IChatQueryService chatQueries,
        DataStore store)
    {
        _peer = peer;
        _messaging = messaging;
        _chatQueries = chatQueries;
        _store = store;

        SendMessageCommand =
            new Command(async () => await SendAsync(), () => _sessionReady);

        _messaging.MessageReceived += OnMessageReceived;
        _messaging.SessionStarted += OnSessionStarted;
        _messaging.SessionClosed += OnSessionClosed;

        _ = InitAsync();
    }

    // ---------------------------
    // Initialization
    // ---------------------------

    private async Task InitAsync()
    {
        _localPeerDbId = await _chatQueries.GetLocalPeerIdAsync();
        await LoadConversationsAsync();

        var server = _store.Peers.FirstOrDefault(p => p.Role == NodeRole.Server);

        if (server == null)
        {
            StatusMessage = "No server discovered";
            return;
        }

        StatusMessage = "Connecting to server...";

        // Background connection
        _ = ConnectInBackgroundAsync(server.ProtocolPeerId);
    }

    private async Task ConnectInBackgroundAsync(string protocolPeerId)
    {
        try
        {
            await _messaging.EnsureSessionAsync(protocolPeerId);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BACKGROUND CONNECT FAILED]");
            Console.WriteLine(ex);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = "Server offline";
            });
        }
    }

    // ---------------------------
    // Session Events
    // ---------------------------

    private void OnSessionStarted(string remoteProtocolPeerId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_activeProtocolPeerId == remoteProtocolPeerId)
                SetSessionConnected();
        });
    }

    private void OnSessionClosed(string remoteProtocolPeerId)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_activeProtocolPeerId != remoteProtocolPeerId)
                return;

            SetSessionDisconnected("Peer disconnected.");

            await TransientNotificationService.ShowAsync(
                "Peer disconnected.",
                NotificationSeverity.Warning,
                5000);
        });
    }

    private void SetSessionConnected()
    {
        _sessionReady = true;
        IsConnected = true;
        StatusMessage = "Connected";
        ((Command)SendMessageCommand).ChangeCanExecute();
    }

    private void SetSessionDisconnected(string status)
    {
        _sessionReady = false;
        IsConnected = false;
        StatusMessage = status;
        ((Command)SendMessageCommand).ChangeCanExecute();
    }

    // ---------------------------
    // Activate Conversation
    // ---------------------------

    public async Task ActivateConversationFromUIAsync(ConversationItem convo)
    {
        if (_activeConversation == convo)
            return;

        _activeConversation = convo;
        _activeProtocolPeerId = convo.Peer.ProtocolPeerId;

        SetSessionDisconnected("Connecting…");

        await LoadChatHistoryAsync(convo.Peer);

        // Background connection
        _ = ConnectInBackgroundAsync(convo.Peer.ProtocolPeerId);
    }

    // ---------------------------
    // Sending
    // ---------------------------

    private async Task SendAsync()
    {
        if (!_sessionReady || _activeConversation == null)
            return;

        var text = OutgoingMessage.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        OutgoingMessage = string.Empty;

        try
        {
            await _messaging.SendMessageAsync(
                _activeConversation.Peer.ProtocolPeerId,
                text);
        }
        catch
        {
            SetSessionDisconnected("Connection lost.");

            await TransientNotificationService.ShowAsync(
                "Connection lost.",
                NotificationSeverity.Error);
        }
    }

    // ---------------------------
    // Incoming Messages
    // ---------------------------

    private void OnMessageReceived(ChatMessage msg)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_activeProtocolPeerId == null)
                return;

            if (msg.FromPeer != _activeProtocolPeerId &&
                msg.ToPeer != _activeProtocolPeerId)
                return;

            Messages.Add(msg);
            MessagesChanged?.Invoke();
        });
    }

    // ---------------------------
    // Data Loading
    // ---------------------------

    private async Task LoadConversationsAsync()
    {
        var historyPeers = await _chatQueries.GetPeersWithMessagesAsync();

        Conversations.Clear();

        foreach (var peer in historyPeers)
            Conversations.Add(new ConversationItem(peer));
    }

    private async Task LoadChatHistoryAsync(PeerNode peer)
    {
        if (_localPeerDbId is null)
            return;

        Messages.Clear();

        var history = await _chatQueries.GetHistoryAsync(
            _localPeerDbId.Value,
            peer.PeerId);

        foreach (var msg in ChatMessageMapper.FromHistoryList(
                     history,
                     _localPeerDbId.Value,
                     _peer.PeerId,
                     peer.ProtocolPeerId))
        {
            Messages.Add(msg);
        }

        MessagesChanged?.Invoke();
    }
}