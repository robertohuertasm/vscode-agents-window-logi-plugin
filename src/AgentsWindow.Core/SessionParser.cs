using System.Text.Json;

namespace AgentsWindow;

// Parses `listSessions` items from raw JSON. The .NET client's SessionSummary type
// (1.0.0) lacks `chats` and `defaultChat`, which the host sends.
public static class SessionParser
{
    public static Session? Parse(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) return null;
        var resource = Str(item, "resource");
        if (resource is null) return null;
        var status = Uint(item, "status");
        var modified = DateTimeOffset.TryParse(Str(item, "modifiedAt"), out var m) ? m : DateTimeOffset.MinValue;
        string? project = item.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.Object ? Str(p, "displayName") : null;
        var defaultChat = Str(item, "defaultChat");

        var chats = new List<Chat>();
        if (item.TryGetProperty("chats", out var cs) && cs.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in cs.EnumerateArray())
            {
                var chatResource = Str(c, "resource");
                if (chatResource is null || Str(c, "interactivity") == "hidden") continue;
                if (Status.IsArchivedFlag(Uint(c, "status"))) continue;
                chats.Add(new Chat(chatResource, Links.ChatId(chatResource, defaultChat), Str(c, "title") ?? "", Status.ToKeyState(Uint(c, "status"))));
            }
        }

        return new Session(resource, Str(item, "title") ?? "", project, Status.ToKeyState(status), Status.IsArchivedFlag(status), modified, chats);
    }

    public static IReadOnlyList<Session> ParseList(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return items.EnumerateArray().Select(Parse).OfType<Session>().ToList();
    }

    public static string? NextCursor(JsonElement result) =>
        result.ValueKind == JsonValueKind.Object ? Str(result, "nextCursor") : null;

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static uint Uint(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetUInt32(out var v) ? v : 0;
}
