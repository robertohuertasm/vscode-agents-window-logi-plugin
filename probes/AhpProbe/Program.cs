// Read-only probe of the VS Code Agent Host local endpoint.
// Prints structure and aggregate counts only: no titles, IDs, paths, or tokens.
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.AgentHostProtocol;

var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "Library", "Application Support", "Code");
var entriesDir = Path.Combine(userData, "agent-host", "local-endpoint", "entries");

var entry = FindLiveEntry(entriesDir);
if (entry is null)
{
    Console.WriteLine("No live agent host entry found.");
    return 1;
}
Console.WriteLine($"Entry: type={entry.Value.Type} schema=2 protocol={entry.Value.Protocol}");

var ws = new ClientWebSocket();
var handler = new SocketsHttpHandler
{
    ConnectCallback = async (_, ct) =>
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(entry.Value.SocketPath), ct);
        return new NetworkStream(socket, ownsSocket: true);
    },
};
var uri = new Uri($"ws://localhost/?tkn={Uri.EscapeDataString(entry.Value.Token)}");
var sw = Stopwatch.StartNew();
await ws.ConnectAsync(uri, new HttpMessageInvoker(handler), CancellationToken.None);
Console.WriteLine($"WebSocket connected in {sw.ElapsedMilliseconds} ms");

await using var client = AhpClient.Connect(WebSocketTransport.FromClientWebSocket(ws));
var init = await client.InitializeAsync(clientId: $"ahp-probe-{Guid.NewGuid():N}");
Console.WriteLine($"Negotiated protocol: {init.ProtocolVersion}");

var sessions = new List<SessionSummary>();
string? cursor = null;
do
{
    var page = await client.RequestAsync<ListSessionsParams, ListSessionsResult>("listSessions",
        new ListSessionsParams { Channel = ProtocolVersion.RootResourceUri, Limit = 100, Cursor = cursor });
    if (page is null) break;
    sessions.AddRange(page.Items);
    cursor = page.NextCursor;
} while (cursor is not null);

Console.WriteLine($"Sessions: {sessions.Count}");
Console.WriteLine("By provider:");
foreach (var g in sessions.GroupBy(s => s.Provider).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine("By status (raw value = flags):");
foreach (var g in sessions.GroupBy(s => s.Status).OrderBy(g => (uint)g.Key))
    Console.WriteLine($"  {(uint)g.Key,3} = {Describe(g.Key)}: {g.Count()}");

Console.WriteLine("Resource URI schemes:");
foreach (var g in sessions.GroupBy(s => new Uri(s.Resource).Scheme))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine($"With project: {sessions.Count(s => s.Project is not null)}, distinct projects: {sessions.Where(s => s.Project is not null).Select(s => s.Project!.Uri).Distinct().Count()}");
Console.WriteLine($"With activity text: {sessions.Count(s => !string.IsNullOrEmpty(s.Activity))}");
var metaKeys = sessions.Where(s => s.Meta is not null).SelectMany(s => s.Meta!.Keys).Distinct().OrderBy(k => k);
Console.WriteLine($"_meta keys: {string.Join(", ", metaKeys)}");
var recent = sessions.Count(s => DateTimeOffset.Parse(s.ModifiedAt) > DateTimeOffset.UtcNow.AddHours(-1));
Console.WriteLine($"Modified in the last hour: {recent}");
Console.WriteLine($"Pages: listed most-recent first = {sessions.Zip(sessions.Skip(1)).All(p => string.CompareOrdinal(p.First.ModifiedAt, p.Second.ModifiedAt) >= 0)}");
return 0;

static string Describe(SessionStatus s)
{
    var v = (uint)s;
    var parts = new List<string>();
    if ((v & 24) == 24) parts.Add("InputNeeded");
    else if ((v & 8) != 0) parts.Add("InProgress");
    if ((v & 1) != 0) parts.Add("Idle");
    if ((v & 2) != 0) parts.Add("Error");
    if ((v & 32) != 0) parts.Add("IsRead");
    if ((v & 64) != 0) parts.Add("IsArchived");
    return string.Join("|", parts);
}

static (string Type, string Protocol, string SocketPath, string Token)? FindLiveEntry(string dir)
{
    if (!Directory.Exists(dir)) return null;
    foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var e = doc.RootElement;
            if (e.GetProperty("schemaVersion").GetInt32() != 2) continue;
            var ep = e.GetProperty("endpoint");
            if (ep.GetProperty("type").GetString() != "socket") continue;
            var pid = e.GetProperty("pid").GetInt32();
            try { if (Process.GetProcessById(pid).HasExited) continue; } catch (ArgumentException) { continue; }
            return (e.GetProperty("type").GetString()!, e.GetProperty("protocolVersion").GetString()!,
                ep.GetProperty("path").GetString()!, e.GetProperty("connectionToken").GetString()!);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
        }
    }
    return null;
}
