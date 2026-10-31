namespace Loupedeck.AhpLoadProbePlugin;

using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AgentHostProtocol;

public class AhpLoadProbeApplication : ClientApplication
{
    protected override string GetProcessName() => "";

    protected override string GetBundleName() => "";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}

public class AhpLoadProbePlugin : Plugin
{
    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => true;

    public override void Load()
    {
        this.Log.Info($"AhpLoadProbe loaded. Runtime {Environment.Version}, AHP client {typeof(AhpClient).Assembly.GetName().Version}");
        _ = Task.Run(this.ProbeAsync);
    }

    public override void Unload()
    {
    }

    private async Task ProbeAsync()
    {
        try
        {
            var entries = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "Code", "agent-host", "local-endpoint", "entries");
            var (socketPath, token) = FindLiveEntry(entries) ?? throw new InvalidOperationException("No live agent host entry");

            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, ct) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
                    return new NetworkStream(socket, ownsSocket: true);
                },
            };
            var ws = new ClientWebSocket();
            var sw = Stopwatch.StartNew();
            await ws.ConnectAsync(new Uri($"ws://localhost/?tkn={Uri.EscapeDataString(token)}"), new HttpMessageInvoker(handler), CancellationToken.None);

            await using var client = AhpClient.Connect(WebSocketTransport.FromClientWebSocket(ws));
            var init = await client.InitializeAsync(clientId: $"ahp-load-probe-{Guid.NewGuid():N}");
            var page = await client.RequestAsync<ListSessionsParams, ListSessionsResult>("listSessions",
                new ListSessionsParams { Channel = ProtocolVersion.RootResourceUri, Limit = 200 });
            var items = page?.Items ?? [];
            var live = items.Count(s => ((uint)s.Status & 64) == 0);
            var working = items.Count(s => ((uint)s.Status & 8) != 0);
            this.Log.Info($"AhpLoadProbe OK in {sw.ElapsedMilliseconds} ms: protocol {init.ProtocolVersion}, {items.Count} sessions, {live} not archived, {working} working or waiting");
        }
        catch (Exception ex)
        {
            this.Log.Error($"AhpLoadProbe failed: {ex.GetType().FullName}: {ex.Message}");
        }
    }

    private static (string SocketPath, string Token)? FindLiveEntry(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var e = doc.RootElement;
                var ep = e.GetProperty("endpoint");
                if (e.GetProperty("schemaVersion").GetInt32() != 2 || ep.GetProperty("type").GetString() != "socket")
                {
                    continue;
                }

                try
                {
                    if (Process.GetProcessById(e.GetProperty("pid").GetInt32()).HasExited)
                    {
                        continue;
                    }
                }
                catch (ArgumentException)
                {
                    continue;
                }

                return (ep.GetProperty("path").GetString()!, e.GetProperty("connectionToken").GetString()!);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
            }
        }

        return null;
    }
}
