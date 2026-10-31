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
            return sessions;
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    private static async Task<AhpClient> ConnectAsync(Endpoint endpoint, CancellationToken cancel)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
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
