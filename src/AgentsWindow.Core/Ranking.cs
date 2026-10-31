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
}
