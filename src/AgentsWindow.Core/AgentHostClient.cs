using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AgentHostProtocol;

namespace AgentsWindow;

public interface ISessionSource
{
    Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel);
}

// Keeps one AHP connection to the local Agent Host and reconnects when it drops or the
// host changes (VS Code restart). Never logs or stores the connection token.
public sealed class AgentHostClient(string entriesDirectory) : ISessionSource, IAsyncDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private AhpClient? client;
    private Endpoint? endpoint;

    public async Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel)
    {
        var current = EndpointRegistry.FindLive(entriesDirectory)
            ?? throw new InvalidOperationException("VS Code's Agent Host is not running");
        if (client is null || endpoint != current)
        {
            await DisconnectAsync();
            client = await ConnectAsync(current, cancel);
            endpoint = current;
        }

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
