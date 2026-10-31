using System.IO.Pipes;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AgentHostProtocol;

namespace AgentsWindow;

public interface ISessionSource
{
    Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel);
}

public interface ISessionActions
{
    Task SetSessionArchivedAsync(string sessionResource, bool archived, CancellationToken cancel);

    Task SetChatArchivedAsync(string chatResource, bool archived, CancellationToken cancel);

    // Creates an empty chat in the session and returns its resource.
    Task<string> CreateChatAsync(string sessionResource, string? defaultChat, CancellationToken cancel);
}

// Keeps one AHP connection to the local Agent Host and reconnects when it drops or the
// host changes (VS Code restart). Never logs or stores the connection token.
public sealed class AgentHostClient(string entriesDirectory) : ISessionSource, ISessionActions, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private AhpClient? client;
    private Endpoint? endpoint;

    // Per chat: the summary fingerprint last checked for an interrupted turn, and the result.
    private readonly Dictionary<string, (string Fingerprint, bool Stuck)> interrupted = [];

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            return await ListLockedAsync(cancel);
        }
        finally
        {
            gate.Release();
        }
    }

    // Archive actions go to the session's or chat's own channel, as the Agents window sends them.
    // The host only reduces actions on channels the client has subscribed to, so each
    // dispatch subscribes to the target channel for its duration.
    public Task SetSessionArchivedAsync(string sessionResource, bool archived, CancellationToken cancel) =>
        DispatchSubscribedAsync(sessionResource, new SessionIsArchivedChangedAction { IsArchived = archived }, cancel);

    public Task SetChatArchivedAsync(string chatResource, bool archived, CancellationToken cancel) =>
        DispatchSubscribedAsync(chatResource, new ChatIsArchivedChangedAction { IsArchived = archived }, cancel);

    private Task DispatchSubscribedAsync(string channel, object action, CancellationToken cancel) =>
        WithClientAsync(async c =>
        {
            await c.SubscribeAsync(channel, cancellationToken: cancel);
            try
            {
                return await c.DispatchAsync(channel, new StateAction(action), cancellationToken: cancel);
            }
            finally
            {
                await c.UnsubscribeAsync(channel, CancellationToken.None);
            }
        }, cancel);

    public async Task<string> CreateChatAsync(string sessionResource, string? defaultChat, CancellationToken cancel)
    {
        // The host only creates chats in sessions it has loaded, which subscribing does.
        var chat = ChatUris.MintPeer(sessionResource, defaultChat);
        await WithClientAsync(async c =>
        {
            await c.SubscribeAsync(sessionResource, cancellationToken: cancel);
            try
            {
                return await c.RequestAsync<CreateChatParams, JsonElement>("createChat",
                    new CreateChatParams { Channel = sessionResource, Chat = chat }, cancel);
            }
            finally
            {
                await c.UnsubscribeAsync(sessionResource, CancellationToken.None);
            }
        }, cancel);
        return chat;
    }

    private async Task WithClientAsync<T>(Func<AhpClient, Task<T>> action, CancellationToken cancel)
    {
        await gate.WaitAsync(cancel);
        try
        {
            await action(await EnsureConnectedAsync(cancel));
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<AhpClient> EnsureConnectedAsync(CancellationToken cancel)
    {
        var current = EndpointRegistry.FindLive(entriesDirectory)
            ?? throw new InvalidOperationException("VS Code's Agent Host is not running");
        if (client is null || endpoint != current)
        {
            await DisconnectAsync();
            client = await ConnectAsync(current, cancel);
            endpoint = current;
        }
        return client;
    }

    private async Task<IReadOnlyList<Session>> ListLockedAsync(CancellationToken cancel)
    {
        var client = await EnsureConnectedAsync(cancel);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(RequestTimeout);
            var sessions = new List<Session>();
            string? cursor = null;
            do
            {
                var page = await client.RequestAsync<ListSessionsParams, JsonElement>("listSessions",
                    new ListSessionsParams { Channel = ProtocolVersion.RootResourceUri, Limit = 200, Cursor = cursor }, timeout.Token);
                sessions.AddRange(SessionParser.ParseList(page));
                cursor = SessionParser.NextCursor(page);
            }
            while (cursor is not null);
            await this.RefreshInterruptedAsync(client, sessions, cancel);
            var stuck = this.interrupted.Where(e => e.Value.Stuck).Select(e => e.Key).ToHashSet();
            return InterruptedTurns.Apply(sessions, stuck);
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    // Checks idle chats of non-archived sessions whose summary changed since the last check.
    // In steady state nothing changes, so no snapshot is fetched. At most a few chats are
    // checked per poll, within their own time budget, and a failure never fails the list:
    // the rest are picked up on later polls.
    private const int MaxInterruptedChecksPerPoll = 3;
    private static readonly TimeSpan InterruptedCheckBudget = TimeSpan.FromSeconds(2);

    private async Task RefreshInterruptedAsync(AhpClient client, IReadOnlyList<Session> sessions, CancellationToken cancel)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        budget.CancelAfter(InterruptedCheckBudget);
        var checkedThisPoll = 0;
        var candidates = sessions.Where(s => !s.Archived)
            .SelectMany(s => s.Chats.Select(c => (Chat: c, Fingerprint: $"{s.ModifiedAt:O}|{c.State}|{c.Title}")))
            .Where(x => x.Chat.State is KeyState.Idle or KeyState.Unread)
            .ToList();
        var live = candidates.Select(x => x.Chat.Resource).ToHashSet();
        foreach (var gone in this.interrupted.Keys.Where(k => !live.Contains(k)).ToList())
        {
            this.interrupted.Remove(gone);
        }

        foreach (var (chat, fingerprint) in candidates)
        {
            if (this.interrupted.TryGetValue(chat.Resource, out var known) && known.Fingerprint == fingerprint) continue;
            if (checkedThisPoll++ >= MaxInterruptedChecksPerPoll || budget.IsCancellationRequested) return;
            var stuck = false;
            try
            {
                var snapshot = await client.RequestAsync<SubscribeParams, JsonElement>("subscribe", new SubscribeParams { Channel = chat.Resource }, budget.Token);
                stuck = InterruptedTurns.EndsResumable(snapshot);
                await client.RequestAsync<UnsubscribeParams, JsonElement>("unsubscribe", new UnsubscribeParams { Channel = chat.Resource }, budget.Token);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // Retried on a later poll: the fingerprint is not recorded.
                Log.Warning($"could not check a chat for an interrupted turn: {e.Message}");
                return;
            }
            if (stuck && known.Stuck != stuck) Log.Info("a chat stopped on a resumable error (Keep Going); showing it as error");
            this.interrupted[chat.Resource] = (fingerprint, stuck);
        }
    }

    private static async Task<AhpClient> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = (_, ct) => OpenStreamAsync(endpoint.SocketPath, ct),
        };
        var ws = new ClientWebSocket();
        await ws.ConnectAsync(new Uri($"ws://localhost/?tkn={Uri.EscapeDataString(endpoint.Token)}"), new HttpMessageInvoker(handler), cancel);
        var ahp = AhpClient.Connect(WebSocketTransport.FromClientWebSocket(ws));
        try
        {
            var init = await ahp.InitializeAsync(clientId: $"vscode-agents-window-logi-plugin-{Guid.NewGuid():N}", cancellationToken: cancel);
            Log.Info($"connected to VS Code's Agent Host ({endpoint.Type}, protocol {init.ProtocolVersion})");
            return ahp;
        }
        catch
        {
            await ahp.DisposeAsync();
            throw;
        }
    }

    private static async ValueTask<Stream> OpenStreamAsync(string path, CancellationToken cancel)
    {
        if (EndpointRegistry.PipeName(path) is { } pipe)
        {
            var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await client.ConnectAsync(cancel);
                return client;
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), cancel);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task DisconnectAsync()
    {
        var old = client;
        client = null;
        endpoint = null;
        if (old is not null)
        {
            try
            {
                await old.DisposeAsync();
            }
            catch (Exception e)
            {
                Log.Warning($"closing the Agent Host connection failed: {e.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
