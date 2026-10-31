using System.Diagnostics;
using System.Text.Json;

namespace AgentsWindow;

public sealed record Endpoint(string Type, int Pid, string SocketPath, string Token);

// Finds the local Agent Host endpoint VS Code publishes for out-of-process clients.
// Format: src/vs/platform/agentHost/LOCAL_ENDPOINT.md. Every entry is untrusted input.
public static class EndpointRegistry
{
    public static string DefaultEntriesDirectory(string product = "Code") =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", product, "agent-host", "local-endpoint", "entries");

    // Prefers the editor's own host over a standalone CLI host.
    public static Endpoint? FindLive(string entriesDirectory, Func<int, bool>? isAlive = null)
    {
        isAlive ??= IsProcessAlive;
        if (!Directory.Exists(entriesDirectory)) return null;
        return Directory.EnumerateFiles(entriesDirectory, "*.json")
            .Select(TryParse)
            .OfType<Endpoint>()
            .Where(e => isAlive(e.Pid))
            .OrderBy(e => e.Type == "editor" ? 0 : 1)
            .FirstOrDefault();
    }

    public static Endpoint? TryParse(string file)
    {
        try
        {
            return Parse(File.ReadAllText(file));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static Endpoint? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object) return null;
            if (!e.TryGetProperty("schemaVersion", out var v) || v.ValueKind != JsonValueKind.Number || v.GetInt32() != 2) return null;
            if (!e.TryGetProperty("endpoint", out var ep) || ep.ValueKind != JsonValueKind.Object) return null;
            if (Str(ep, "type") != "socket") return null;
            var path = Str(ep, "path");
            var token = Str(e, "connectionToken");
            var type = Str(e, "type");
            if (path is null || token is null || type is null) return null;
            if (!e.TryGetProperty("pid", out var pid) || pid.ValueKind != JsonValueKind.Number) return null;
            return new Endpoint(type, pid.GetInt32(), path, token);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
