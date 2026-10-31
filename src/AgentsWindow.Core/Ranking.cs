namespace AgentsWindow;

// Orders sessions for the keys: active states first, then most recently modified.
// A session only moves when its state or modification time changes, never on focus,
// so a key does not reorder under the finger that pressed it.
public static class Ranking
{
    public static IReadOnlyList<Session> Rank(IEnumerable<Session> sessions) =>
        sessions
            .Where(s => !s.Archived)
            .OrderBy(s => Status.Priority(s.State))
            .ThenByDescending(s => s.ModifiedAt)
            .ThenBy(s => s.Resource, StringComparer.Ordinal)
            .ToList();

    // Every visible chat of every ranked session, for the Recent keys: a session with
    // several active chats should take several keys. Same priority as sessions; chats
    // have no modification time, so equal states keep their session's order and then
    // the session's own chat order (main chat first).
    public static IReadOnlyList<RankedChat> RankChats(IReadOnlyList<Session> rankedSessions) =>
        rankedSessions
            .SelectMany((s, si) => ChatsOf(s).Select((c, ci) => (Entry: new RankedChat(s, c), si, ci)))
            .OrderBy(x => Status.Priority(x.Entry.Chat.State))
            .ThenBy(x => x.si)
            .ThenBy(x => x.ci)
            .Select(x => x.Entry)
            .ToList();

    // A session whose catalog is empty still gets a key, standing for its main chat.
    private static IEnumerable<Chat> ChatsOf(Session s) =>
        s.Chats.Count > 0 ? s.Chats : [new Chat(s.DefaultChat ?? s.Resource, null, s.Title, s.State)];
}

public sealed record RankedChat(Session Session, Chat Chat)
{
    public bool IsMain => this.Chat.ChatId is null;

    // The main chat shares its session's title, so it reads as the session.
    public string Title => this.IsMain ? this.Session.Title : this.Chat.Title;
}
