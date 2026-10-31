namespace AgentsWindow;

// Polls the Agent Host and keeps the ranked session list current. `Changed` fires
// only when something a key shows (order, title, state, chats, online) changed.
public sealed class SessionBoard(ISessionSource source)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private string signature = "";
    private Dictionary<string, KeyState> lastStates = [];

    public IReadOnlyList<Session> Ranked { get; private set; } = [];

    public bool Online { get; private set; }

    public event Action? Changed;

    public Session? At(int index) => index >= 0 && index < Ranked.Count ? Ranked[index] : null;

    public Session? Find(string resource) => Ranked.FirstOrDefault(s => s.Resource == resource);

    // Null means the source is unreachable. Returns whether anything visible changed.
    public bool Apply(IReadOnlyList<Session>? sessions)
    {
        var ranked = sessions is null ? [] : Ranking.Rank(sessions);
        var online = sessions is not null;
        if (sessions is not null) this.LogTransitions(ranked);
        var next = Signature(ranked, online);
        Ranked = ranked;
        Online = online;
        if (next == signature) return false;
        signature = next;
        return true;
    }

    public async Task RunAsync(CancellationToken cancel)
    {
        var first = true;
        while (!cancel.IsCancellationRequested)
        {
            IReadOnlyList<Session>? sessions = null;
            try
            {
                sessions = await source.ListSessionsAsync(cancel);
                if (!Online && !first) Log.Info("VS Code's Agent Host is reachable again");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                if (Online || first) Log.Warning($"VS Code's Agent Host is unreachable, keys show offline: {e.Message}");
            }

            // The service keeps faces from an earlier run, so the first poll always redraws.
            if (Apply(sessions) || first) Changed?.Invoke();
            first = false;

            try
            {
                await Task.Delay(PollInterval, cancel);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // One line per state change, with a short id and no title, to compare with what the keys show.
    private void LogTransitions(IReadOnlyList<Session> ranked)
    {
        var states = ranked.ToDictionary(s => s.Resource, s => s.State);
        if (lastStates.Count > 0)
        {
            foreach (var (resource, state) in states)
            {
                if (lastStates.TryGetValue(resource, out var before) && before != state)
                {
                    var rank = ranked.ToList().FindIndex(s => s.Resource == resource) + 1;
                    Log.Info($"session {ShortId(resource)} {before} -> {state} (now #{rank})");
                }
            }
        }
        lastStates = states;
    }

    private static string ShortId(string resource) => resource.Length > 8 ? resource[^8..] : resource;

    private static string Signature(IReadOnlyList<Session> ranked, bool online) =>
        (online ? "on" : "off") + "\n" + string.Join("\n", ranked.Select(s =>
            $"{s.Resource}\t{s.Title}\t{s.State}\t" + string.Join(",", s.Chats.Select(c => $"{c.Resource}|{c.Title}|{c.State}"))));
}
