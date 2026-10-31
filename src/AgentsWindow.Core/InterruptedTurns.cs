using System.Text.Json;

namespace AgentsWindow;

// VS Code can leave a chat whose last turn stopped on a resumable error (the blue
// "Keep Going" / "Try Again" button) with an idle summary status, for example after it
// restarts mid-turn. The summary list does not carry turns, so this reads a chat's
// snapshot, but only when that chat's summary changed: snapshots can weigh a megabyte.
public static class InterruptedTurns
{
    // True when the chat snapshot's last turn ended in an error the host can resume.
    public static bool EndsResumable(JsonElement subscribeResult)
    {
        if (!subscribeResult.TryGetProperty("snapshot", out var snapshot)
            || !snapshot.TryGetProperty("state", out var state)
            || !state.TryGetProperty("turns", out var turns)
            || turns.ValueKind != JsonValueKind.Array
            || turns.GetArrayLength() == 0)
        {
            return false;
        }

        if (state.TryGetProperty("activeTurn", out var active) && active.ValueKind == JsonValueKind.Object)
        {
            return false;
        }

        var last = turns[turns.GetArrayLength() - 1];
        if (!last.TryGetProperty("state", out var turnState) || turnState.GetString() != "error")
        {
            return false;
        }

        if (!last.TryGetProperty("responseParts", out var parts) || parts.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("kind", out var kind) && kind.GetString() == "error"
                && part.TryGetProperty("resumable", out var resumable) && resumable.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        return false;
    }

    // Upgrades idle chats known to be stuck on a resumable error to Error, and recomputes
    // each session's state from its chats. Other states are left as VS Code reports them.
    public static IReadOnlyList<Session> Apply(IReadOnlyList<Session> sessions, IReadOnlySet<string> stuckChats)
    {
        if (stuckChats.Count == 0) return sessions;
        return sessions.Select(s =>
        {
            if (!s.Chats.Any(c => IsStuck(c, stuckChats))) return s;
            var chats = s.Chats.Select(c => IsStuck(c, stuckChats) ? c with { State = KeyState.Error } : c).ToList();
            return s with { Chats = chats, State = Status.MostUrgent(s.State, chats.Select(c => c.State)) };
        }).ToList();
    }

    // Only an idle or unread chat can be stuck: any newer activity wins over the old error.
    private static bool IsStuck(Chat chat, IReadOnlySet<string> stuckChats) =>
        chat.State is KeyState.Idle or KeyState.Unread && stuckChats.Contains(chat.Resource);
}
