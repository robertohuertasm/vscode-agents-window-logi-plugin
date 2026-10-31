using AgentsWindow;

namespace AgentsWindow.Tests;

public class ChatRankingTests
{
    private static Chat C(string id, KeyState state, bool main = false) =>
        new($"ahp-chat:/{id}", main ? null : id, id, state, CanArchive: !main);

    private static Session S(string id, int minutesAgo, params Chat[] chats) =>
        new($"ahp-session:/{id}", id, null, chats.Select(c => c.State).OrderBy(Status.Priority).FirstOrDefault(KeyState.Idle),
            false, DateTimeOffset.UnixEpoch.AddHours(1).AddMinutes(-minutesAgo), chats);

    [Fact]
    public void CountsEveryChatAndPutsActiveOnesFirst()
    {
        var sessions = Ranking.Rank([
            S("a", 1, C("a-main", KeyState.Idle, main: true), C("a-side", KeyState.InputNeeded)),
            S("b", 5, C("b-main", KeyState.Working, main: true)),
            S("c", 2, C("c-main", KeyState.Idle, main: true)),
        ]);

        var chats = Ranking.RankChats(sessions).Select(c => c.Chat.Title).ToList();

        Assert.Equal(["a-side", "b-main", "a-main", "c-main"], chats);
    }

    [Fact]
    public void EqualStatesKeepSessionThenChatOrder()
    {
        var sessions = Ranking.Rank([
            S("old", 10, C("old-main", KeyState.Working, main: true)),
            S("new", 1, C("new-main", KeyState.Working, main: true), C("new-side", KeyState.Working)),
        ]);

        Assert.Equal(["new-main", "new-side", "old-main"], Ranking.RankChats(sessions).Select(c => c.Chat.Title));
    }

    [Fact]
    public void MainChatReadsAsItsSession()
    {
        var session = S("Reducir tamaño", 1, C("main", KeyState.Idle, main: true), C("Problema con icono", KeyState.Idle));
        var chats = Ranking.RankChats([session]);

        Assert.True(chats[0].IsMain);
        Assert.Equal("Reducir tamaño", chats[0].Title);
        Assert.False(chats[1].IsMain);
        Assert.Equal("Problema con icono", chats[1].Title);
    }

    [Fact]
    public void SessionWithoutChatsStillGetsAKey()
    {
        var session = new Session("copilotcli:/x", "Lonely", null, KeyState.Working, false, DateTimeOffset.UnixEpoch, []);
        var chat = Assert.Single(Ranking.RankChats([session]));

        Assert.True(chat.IsMain);
        Assert.Equal(KeyState.Working, chat.Chat.State);
        Assert.Equal("Lonely", chat.Title);
    }

    [Fact]
    public void BoardExposesChatsBySlot()
    {
        var board = new SessionBoard(new NoSource());
        board.Apply([S("a", 1, C("a-main", KeyState.Idle, main: true), C("a-side", KeyState.Working))]);

        Assert.Equal("a-side", board.ChatAt(0)?.Chat.Title);
        Assert.Equal("a-main", board.ChatAt(1)?.Chat.Title);
        Assert.Null(board.ChatAt(2));
        Assert.Equal("a-side", board.FindChat("ahp-chat:/a-side")?.Chat.Title);
    }

    private sealed class NoSource : ISessionSource
    {
        public Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel) => Task.FromResult<IReadOnlyList<Session>>([]);
    }
}
