using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http.Json;
using System.Net.Sockets;
using ZCL.API;
using ZCL.Models;
using ZCL.Protocol.ZCSP;
using ZCL.Protocol.ZCSP.Protocol;
using ZCL.Protocol.ZCSP.Transport;
using ZCL.Repositories.Peers;

namespace ZCL.Services.LLM;

public sealed class LLMChatService : IZcspService
{
    public string ServiceName => "LLMChat";

    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<Guid, NetworkStream> _sessions = new();
    private readonly IServiceScopeFactory _scopeFactory;

    // Needed for routing (server-first, fallback direct)
    private readonly ZcspPeer _peer;
    private readonly RoutingState _routingState;
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private Guid? _serverSessionId;

    // hostProtocolPeerId -> sessionId
    private readonly ConcurrentDictionary<string, Guid> _directSessions = new();

    // sessionId -> stream + remote peer id
    private readonly ConcurrentDictionary<Guid, SessionContext> _contexts = new();

    // requestId -> requesterSessionId (ONLY used when acting as server router)
    private readonly ConcurrentDictionary<Guid, Guid> _pendingRequests = new();

    private sealed record SessionContext(NetworkStream Stream, string RemoteProtocolPeerId);

    // sessionId -> stream + who it's connected to
    private readonly ConcurrentDictionary<Guid, SessionContext> _contexts = new();

    private sealed record SessionContext(NetworkStream Stream, string RemoteProtocolPeerId);
    private sealed record SessionContext(NetworkStream Stream, string RemoteProtocolPeerId);

    private readonly ConcurrentDictionary<Guid, Guid> _sessionConversations = new();

    public event Func<string, Task>? ResponseReceived;
    public event Action<Guid, string>? SessionStarted;

    public LLMChatService(
        ZcspPeer peer,
        RoutingState routingState,
        IServiceScopeFactory scopeFactory)
    {
        _peer = peer;
        _routingState = routingState;
        _scopeFactory = scopeFactory;

        _http = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:11434"),
            Timeout = TimeSpan.FromSeconds(60)
        };
    }

    public async Task OnSessionStartedAsync(Guid sessionId, string remotePeerId, NetworkStream stream)
    {
        _sessions[sessionId] = stream;
        _contexts[sessionId] = new SessionContext(stream, remotePeerId);

        // If we are connecting to the server (in ViaServer mode), remember this as server session
        if (_routingState.Mode == RoutingMode.ViaServer &&
            remotePeerId == _routingState.ServerProtocolPeerId)
        {
            _serverSessionId = sessionId;
        }
        else
        {
            // otherwise it's a direct session to some peer
            _directSessions[remotePeerId] = sessionId;
        }

        SessionStarted?.Invoke(sessionId, remotePeerId);

        // Only create local conversation if THIS node actually has Ollama
        if (await IsOllamaAvailableAsync())
            await TryCreateHostConversationAsync(sessionId, remotePeerId);
    }
    private async Task<bool> IsOllamaAvailableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var res = await _http.GetAsync("/api/tags", cts.Token); // cheap endpoint
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private async Task TryCreateHostConversationAsync(Guid sessionId, string remotePeerId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ServiceDBContext>();
            var peersRepo = scope.ServiceProvider.GetRequiredService<IPeerRepository>();

            var remotePeer = await peersRepo.GetByProtocolPeerIdAsync(remotePeerId);
            if (remotePeer == null)
                return; // if you only want persistence for known peers

            var convo = new LLMConversationEntity
            {
                Id = Guid.NewGuid(),
                PeerId = remotePeer.PeerId,
                Model = "phi3:latest",
                CreatedAt = DateTime.UtcNow
            };

            db.LLMConversations.Add(convo);
            await db.SaveChangesAsync();

            _sessionConversations[sessionId] = convo.Id;
        }
        catch
        {
            // no persistence, still functional
        }
    }

    // File: ZCL.Services.LLM/LLMChatService.cs
    // Function: OnSessionDataAsync(...)

    public async Task OnSessionDataAsync(Guid sessionId, BinaryReader reader)
    {
        var action = BinaryCodec.ReadString(reader);

        switch (action)
        {
            // =========================
            // CLIENT -> HOST (direct)  OR  SERVER -> HOST (forwarded)
            // =========================
            case "AiQuery2":
                {
                    var requestId = BinaryCodec.ReadGuid(reader);
                    var prompt = BinaryCodec.ReadString(reader);
                    await HandleAiQueryAsync(sessionId, requestId, prompt);
                    break;
                }

            // =========================
            // CLIENT -> SERVER (route to host)
            // =========================
            case "AiQueryFor2":
                {
                    var requestId = BinaryCodec.ReadGuid(reader);
                    var targetProtocolPeerId = BinaryCodec.ReadString(reader);
                    var prompt = BinaryCodec.ReadString(reader);

                    // Only servers should route this.
                    if (_routingState.Role != NodeRole.Server)
                        return;

                    await HandleAiQueryForAsync(sessionId, requestId, targetProtocolPeerId, prompt);
                    break;
                }

            // =========================
            // HOST -> CLIENT (direct) OR HOST -> SERVER (to route back)
            // =========================
            case "AiResponse2":
                {
                    var requestId = BinaryCodec.ReadGuid(reader);
                    var response = BinaryCodec.ReadString(reader);

                    // If we are the server router, forward response back to requester
                    if (_routingState.Role == NodeRole.Server &&
                        _pendingRequests.TryRemove(requestId, out var requesterSessionId) &&
                        _contexts.TryGetValue(requesterSessionId, out var requesterCtx))
                    {
                        var forward = BinaryCodec.Serialize(
                            ZcspMessageType.SessionData,
                            requesterSessionId,
                            w =>
                            {
                                BinaryCodec.WriteString(w, "AiResponse2");
                                BinaryCodec.WriteGuid(w, requestId);
                                BinaryCodec.WriteString(w, response);
                            });

                        await Framing.WriteAsync(requesterCtx.Stream, forward);
                        return;
                    }

                    // Normal receive on a client
                    if (ResponseReceived != null)
                    {
                        foreach (Func<string, Task> handler in ResponseReceived.GetInvocationList())
                            await handler(response);
                    }
                    break;
                }
        }
    }

    private async Task HandleAiQueryForAsync(
    Guid requesterSessionId,
    Guid requestId,
    string targetProtocolPeerId,
    string prompt)
    {
        // Only valid on server
        if (_routingState.Role != NodeRole.Server)
            return;

        // 1) Resolve target peer in DB
        using var scope = _scopeFactory.CreateScope();
        var peersRepo = scope.ServiceProvider.GetRequiredService<IPeerRepository>();

        var targetPeer = await peersRepo.GetByProtocolPeerIdAsync(targetProtocolPeerId);
        if (targetPeer == null)
            return;

        // 2) Ensure server -> host session
        await EnsureSessionAsync(targetPeer);

        if (!_directSessions.TryGetValue(targetProtocolPeerId, out var serverToHostSessionId))
            return;

        if (!_contexts.TryGetValue(serverToHostSessionId, out var hostCtx))
            return;

        // 3) Remember where to route the response back to (requestId -> requester session)
        _pendingRequests[requestId] = requesterSessionId;

        // 4) Forward request to host as AiQuery2
        var forward = BinaryCodec.Serialize(
            ZcspMessageType.SessionData,
            serverToHostSessionId,
            w =>
            {
                BinaryCodec.WriteString(w, "AiQuery2");
                BinaryCodec.WriteGuid(w, requestId);
                BinaryCodec.WriteString(w, prompt);
            });

        await Framing.WriteAsync(hostCtx.Stream, forward);
    }


    public Task OnSessionClosedAsync(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _sessionConversations.TryRemove(sessionId, out _);

        if (_contexts.TryRemove(sessionId, out var ctx))
        {
            // remove direct session mapping (remotePeerId -> sessionId)
            _directSessions.TryRemove(ctx.RemoteProtocolPeerId, out _);
        }

        // if server session closed, forget it
        if (_serverSessionId == sessionId)
            _serverSessionId = null;

        return Task.CompletedTask;
    }

    // =========================
    // CLIENT SIDE
    // =========================

    public async Task SendQueryAsync(Guid sessionId, string prompt)
    {
        if (!_sessions.TryGetValue(sessionId, out var stream))
            throw new InvalidOperationException("AI session not active.");

        var msg = BinaryCodec.Serialize(
            ZcspMessageType.SessionData,
            sessionId,
            w =>
            {
                BinaryCodec.WriteString(w, "AiQuery");
                BinaryCodec.WriteString(w, prompt);
            });

        await Framing.WriteAsync(stream, msg);
    }

    // =========================
    // HOST SIDE
    // =========================

    // File: ZCL.Services.LLM/LLMChatService.cs
    // Function: HandleAiQueryAsync(...)

    private async Task HandleAiQueryAsync(Guid sessionId, Guid requestId, string prompt)
    {
        try
        {
            if (!_sessions.TryGetValue(sessionId, out var stream))
                return;

            if (prompt.Length > 4000)
                prompt = prompt[..4000];

            // === HOST-SIDE SAVE: prompt ===
            Guid? convoId = null;

            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ServiceDBContext>();

                if (_sessionConversations.TryGetValue(sessionId, out var cid))
                {
                    convoId = cid;

                    db.LLMMessages.Add(new LLMMessageEntity
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = cid,
                        Content = prompt,
                        IsUser = true,
                        Timestamp = DateTime.UtcNow
                    });

                    await db.SaveChangesAsync();
                }
            }

            // Generate response
            var reply = await GenerateLocalAsync(prompt);

            // === HOST-SIDE SAVE: response ===
            if (convoId.HasValue)
            {
                using var scope2 = _scopeFactory.CreateScope();
                var db2 = scope2.ServiceProvider.GetRequiredService<ServiceDBContext>();

                db2.LLMMessages.Add(new LLMMessageEntity
                {
                    Id = Guid.NewGuid(),
                    ConversationId = convoId.Value,
                    Content = reply,
                    IsUser = false,
                    Timestamp = DateTime.UtcNow
                });

                await db2.SaveChangesAsync();
            }

            // Send response back (IMPORTANT: AiResponse2 + requestId)
            var responseMsg = BinaryCodec.Serialize(
                ZcspMessageType.SessionData,
                sessionId,
                w =>
                {
                    BinaryCodec.WriteString(w, "AiResponse2");
                    BinaryCodec.WriteGuid(w, requestId);
                    BinaryCodec.WriteString(w, reply);
                });

            await Framing.WriteAsync(stream, responseMsg);
        }
        catch (TaskCanceledException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"AI error: {ex.Message}");
        }
    }

    // =========================
    // LOCAL OLLAMA CALL
    // =========================

    public async Task<string> GenerateLocalAsync(string prompt)
    {
        try
        {
            var httpResponse = await _http.PostAsJsonAsync(
                "/api/generate",
                new
                {
                    model = "phi3:latest",
                    prompt = prompt,
                    stream = false
                });

            httpResponse.EnsureSuccessStatusCode();

            var result = await httpResponse.Content
                .ReadFromJsonAsync<OllamaResponse>();

            return result?.Response?.Trim() ?? "No response.";
        }
        catch (HttpRequestException)
        {
            return "AI service unavailable on this peer.";
        }
    }

    public async Task SendQueryRoutedAsync(
    PeerNode? ownerPeer,
    string targetProtocolPeerId,
    string prompt,
    CancellationToken ct = default)
    {
        var (sid, ctx, viaServer) = await GetRouteAsync(ownerPeer, ct);

        var requestId = Guid.NewGuid();

        var msg = BinaryCodec.Serialize(
            ZcspMessageType.SessionData,
            sid,
            w =>
            {
                if (viaServer)
                {
                    BinaryCodec.WriteString(w, "AiQueryFor2");
                    BinaryCodec.WriteGuid(w, requestId);
                    BinaryCodec.WriteString(w, targetProtocolPeerId);
                    BinaryCodec.WriteString(w, prompt);
                }
                else
                {
                    BinaryCodec.WriteString(w, "AiQuery2");
                    BinaryCodec.WriteGuid(w, requestId);
                    BinaryCodec.WriteString(w, prompt);
                }
            });

        await Framing.WriteAsync(ctx.Stream, msg);
    }


    private async Task<(Guid sessionId, SessionContext ctx, bool viaServer)>
GetRouteAsync(PeerNode? directPeer, CancellationToken ct)
    {
        if (_routingState.Mode == RoutingMode.ViaServer)
        {
            if (_serverSessionId is Guid sid && _contexts.TryGetValue(sid, out var sctx))
                return (sid, sctx, true);

            if (await EnsureServerSessionAsync(ct) &&
                _serverSessionId is Guid sid2 &&
                _contexts.TryGetValue(sid2, out var sctx2))
            {
                return (sid2, sctx2, true);
            }
        }

        if (directPeer == null)
            throw new InvalidOperationException("No server and no direct peer.");

        if (_directSessions.TryGetValue(directPeer.ProtocolPeerId, out var dsid) &&
            _contexts.TryGetValue(dsid, out var dctx))
            return (dsid, dctx, false);

        await EnsureSessionAsync(directPeer, ct);

        if (_directSessions.TryGetValue(directPeer.ProtocolPeerId, out var dsid2) &&
            _contexts.TryGetValue(dsid2, out var dctx2))
            return (dsid2, dctx2, false);

        throw new InvalidOperationException("Direct session failed.");
    }

    public async Task EnsureSessionAsync(PeerNode peer, CancellationToken ct = default)
    {
        if (_directSessions.ContainsKey(peer.ProtocolPeerId))
            return;

        await _sessionLock.WaitAsync(ct);
        try
        {
            if (_directSessions.ContainsKey(peer.ProtocolPeerId))
                return;

            await _peer.ConnectAsync(peer.IpAddress, 5555, peer.ProtocolPeerId, this);
        }
        finally
        {
            _sessionLock.Release();
        }
    }


    public async Task<bool> EnsureServerSessionAsync(CancellationToken ct = default)
    {
        if (_routingState.Mode != RoutingMode.ViaServer)
            return false;

        if (_serverSessionId is Guid sid && _contexts.ContainsKey(sid))
            return true;

        using var scope = _scopeFactory.CreateScope();
        var peers = scope.ServiceProvider.GetRequiredService<IPeerRepository>();

        var server = await peers.GetByProtocolPeerIdAsync(_routingState.ServerProtocolPeerId!);
        if (server == null)
            return false;

        try
        {
            await _peer.ConnectAsync(server.IpAddress, 5555, server.ProtocolPeerId, this);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class OllamaResponse
    {
        public string Response { get; set; } = "";
    }
}
