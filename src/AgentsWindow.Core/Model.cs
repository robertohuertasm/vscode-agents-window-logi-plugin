namespace AgentsWindow;

// What a key shows. Derived from AHP's SessionStatus bit field.
public enum KeyState
{
    None,
    Offline,
    InputNeeded,
    Error,
    Working,
    // Finished but not looked at since: idle without SessionStatus.IsRead. VS Code shows a blue dot.
    Unread,
    Idle,
}

// ChatId is the id used in open links; null for the session's default chat.
// CanArchive is false for chats VS Code refuses to archive on their own: the default chat
// and chats created as side chats or by tools (agentService.ts, _rejectUnknownChatAction).
public sealed record Chat(string Resource, string? ChatId, string Title, KeyState State, bool CanArchive = false);

public sealed record Session(
    string Resource,
    string Title,
    string? Project,
    KeyState State,
    bool Archived,
    DateTimeOffset ModifiedAt,
    IReadOnlyList<Chat> Chats,
    string? DefaultChat = null);

public static class Status
{
    private const uint Idle = 1;
    private const uint Error = 1 << 1;
    private const uint InProgress = 1 << 3;
    private const uint InputNeeded = (1 << 3) | (1 << 4);
    private const uint IsRead = 1 << 5;
    private const uint IsArchived = 1 << 6;

    public static KeyState ToKeyState(uint status) =>
        (status & InputNeeded) == InputNeeded ? KeyState.InputNeeded
        : (status & Error) != 0 ? KeyState.Error
        : (status & InProgress) != 0 ? KeyState.Working
        : (status & IsRead) == 0 ? KeyState.Unread
        : KeyState.Idle;

    public static bool IsArchivedFlag(uint status) => (status & IsArchived) != 0;

    // The session key shows its most urgent chat, so a session whose main chat is idle
    // but has a side chat waiting or unread is not shown as plain idle.
    public static KeyState MostUrgent(KeyState session, IEnumerable<KeyState> chats) =>
        chats.Append(session).OrderBy(Priority).First();

    // Lower ranks first: anything that needs the user beats anything that does not.
    // Unread (finished, not looked at) needs the user; Working does not yet.
    public static int Priority(KeyState state) => state switch
    {
        KeyState.InputNeeded => 0,
        KeyState.Error => 1,
        KeyState.Unread => 2,
        KeyState.Working => 3,
        _ => 4,
    };
}
