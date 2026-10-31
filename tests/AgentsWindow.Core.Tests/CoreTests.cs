using System.Text.Json;
using AgentsWindow;

namespace AgentsWindow.Tests;

public class StatusTests
{
    [Theory]
    [InlineData(33u, KeyState.Idle)]
    [InlineData(1u, KeyState.Unread)]
    [InlineData(40u, KeyState.Working)]
    [InlineData(24u, KeyState.InputNeeded)]
    [InlineData(56u, KeyState.InputNeeded)]
    [InlineData(2u, KeyState.Error)]
    [InlineData(97u, KeyState.Idle)]
    public void MapsStatusBits(uint status, KeyState expected) => Assert.Equal(expected, Status.ToKeyState(status));

    [Fact]
    public void DetectsArchived()
    {
        Assert.True(Status.IsArchivedFlag(97));
        Assert.False(Status.IsArchivedFlag(33));
    }
}

public class MostUrgentTests
{
    [Fact]
    public void SessionTakesItsMostUrgentChat()
    {
        Assert.Equal(KeyState.Unread, Status.MostUrgent(KeyState.Idle, [KeyState.Idle, KeyState.Unread]));
        Assert.Equal(KeyState.InputNeeded, Status.MostUrgent(KeyState.Working, [KeyState.InputNeeded]));
        Assert.Equal(KeyState.Unread, Status.MostUrgent(KeyState.Working, [KeyState.Unread]));
    }
}

public class RankingTests
{
    private static Session S(string id, KeyState state, int minutesAgo, bool archived = false) =>
        new($"ahp-session:/{id}", id, null, state, archived, DateTimeOffset.UnixEpoch.AddHours(1).AddMinutes(-minutesAgo), []);

    [Fact]
    public void ActiveFirstThenMostRecent()
    {
        var ranked = Ranking.Rank([
            S("idle-new", KeyState.Idle, 1),
            S("unread", KeyState.Unread, 60),
            S("working", KeyState.Working, 30),
            S("idle-old", KeyState.Idle, 10),
            S("waiting", KeyState.InputNeeded, 50),
            S("error", KeyState.Error, 40),
        ]);
        Assert.Equal(["waiting", "error", "unread", "working", "idle-new", "idle-old"], ranked.Select(s => s.Title));
    }

    [Fact]
    public void DropsArchived()
    {
        var ranked = Ranking.Rank([S("a", KeyState.Idle, 1, archived: true), S("b", KeyState.Idle, 2)]);
        Assert.Equal(["b"], ranked.Select(s => s.Title));
    }
}

public class LinksTests
{
    [Fact]
    public void OpenSessionLink() =>
        Assert.Equal("vscode://agents/agent-host-session/ahp-session/71a0899e-5e04-4580-9dcf-14d685a669e7",
            Links.OpenSession("ahp-session:/71a0899e-5e04-4580-9dcf-14d685a669e7"));

    [Fact]
    public void OpenChatLinkDoubleEncodesChatId() =>
        Assert.Equal("vscode://agents/agent-host-session/ahp-session/abc/chat/a%252Fb",
            Links.OpenSession("ahp-session:/abc", "a/b"));

    [Fact]
    public void DefaultChatHasNoChatSegment() =>
        Assert.Equal("vscode://agents/agent-host-session/copilotcli/abc", Links.OpenSession("copilotcli:/abc", "default"));

    [Fact]
    public void NewSessionLink() => Assert.Equal("vscode://agents/new?prompt=%20", Links.NewSession(" "));

    [Theory]
    [InlineData("ahp-chat:/2d523c65-e8e3-409f-8671-e098a2b1d71e", null, "2d523c65-e8e3-409f-8671-e098a2b1d71e")]
    [InlineData("ahp-chat://default/YWJj", null, null)]
    [InlineData("ahp-chat://peer1/YWJj", null, "peer1")]
    [InlineData("ahp-chat:/2d523c65-e8e3-409f-8671-e098a2b1d71e", "ahp-chat:/2d523c65-e8e3-409f-8671-e098a2b1d71e", null)]
    public void ChatIds(string resource, string? defaultChat, string? expected) => Assert.Equal(expected, Links.ChatId(resource, defaultChat));
}

public class SessionParserTests
{
    [Fact]
    public void ParsesSessionWithChats()
    {
        var json = """
        {"items":[{"resource":"ahp-session:/s1","title":"T","status":40,"modifiedAt":"2026-10-31T16:00:00Z",
          "project":{"uri":"file:///p","displayName":"proj"},"defaultChat":"ahp-chat:/11111111-1111-1111-1111-111111111111",
          "chats":[
            {"resource":"ahp-chat:/11111111-1111-1111-1111-111111111111","title":"main","status":40},
            {"resource":"ahp-chat:/22222222-2222-2222-2222-222222222222","title":"side","status":33,"origin":{"kind":"user"}},
            {"resource":"ahp-chat:/55555555-5555-5555-5555-555555555555","title":"aside","status":33,"origin":{"kind":"sideChat"}},
            {"resource":"ahp-chat:/33333333-3333-3333-3333-333333333333","title":"worker","status":33,"interactivity":"hidden"},
            {"resource":"ahp-chat:/44444444-4444-4444-4444-444444444444","title":"old","status":97}]}],
         "nextCursor":"c2"}
        """;
        using var doc = JsonDocument.Parse(json);
        var s = Assert.Single(SessionParser.ParseList(doc.RootElement));
        Assert.Equal(KeyState.Working, s.State);
        Assert.Equal("proj", s.Project);
        Assert.Equal(["main", "side", "aside"], s.Chats.Select(c => c.Title));
        Assert.Equal([false, true, false], s.Chats.Select(c => c.CanArchive));
        Assert.Null(s.Chats[0].ChatId);
        Assert.Equal("22222222-2222-2222-2222-222222222222", s.Chats[1].ChatId);
        Assert.Equal("c2", SessionParser.NextCursor(doc.RootElement));
    }

    [Fact]
    public void SkipsMalformedItems()
    {
        using var doc = JsonDocument.Parse("""{"items":[{"title":"no resource"},42,{"resource":"copilotcli:/x"}]}""");
        Assert.Equal(["copilotcli:/x"], SessionParser.ParseList(doc.RootElement).Select(s => s.Resource));
    }
}

public class EndpointTests
{
    private const string Valid = """{"schemaVersion":2,"type":"editor","pid":42,"instanceId":"i","protocolVersion":"1.1.0","connectionToken":"t","endpoint":{"type":"socket","path":"/tmp/x.sock"}}""";

    [Fact]
    public void ParsesSchema2Socket() => Assert.Equal(new Endpoint("editor", 42, "/tmp/x.sock", "t"), EndpointRegistry.Parse(Valid));

    [Theory]
    [InlineData("""{"schemaVersion":1,"endpointPath":"/tmp/x.sock"}""")]
    [InlineData("""{"schemaVersion":2,"type":"standalone","pid":1,"connectionToken":"t","endpoint":{"type":"tcp","host":"h","port":1}}""")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void IgnoresUnsupported(string json) => Assert.Null(EndpointRegistry.Parse(json));

    [Theory]
    [InlineData(@"\\.\pipe\vscode-agent-host-abc-def", "vscode-agent-host-abc-def")]
    [InlineData("/tmp/vscode-ah-1/x.sock", null)]
    public void DetectsWindowsPipes(string path, string? expected) => Assert.Equal(expected, EndpointRegistry.PipeName(path));

    [Fact]
    public void ParsesWindowsPipeEndpoint()
    {
        var json = Valid.Replace("\"/tmp/x.sock\"", "\"\\\\\\\\.\\\\pipe\\\\vscode-agent-host-a\"");
        Assert.Equal(@"\\.\pipe\vscode-agent-host-a", EndpointRegistry.Parse(json)?.SocketPath);
    }

    [Fact]
    public void FindLiveSkipsDeadPids()
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "a.json"), Valid);
            File.WriteAllText(Path.Combine(dir.FullName, "b.json"), Valid.Replace("\"pid\":42", "\"pid\":7"));
            Assert.Equal(7, EndpointRegistry.FindLive(dir.FullName, pid => pid == 7)?.Pid);
            Assert.Null(EndpointRegistry.FindLive(dir.FullName, _ => false));
        }
        finally
        {
            dir.Delete(true);
        }
    }
}

public class SessionBoardTests
{
    private sealed class NoSource : ISessionSource
    {
        public Task<IReadOnlyList<Session>> ListSessionsAsync(CancellationToken cancel) => Task.FromResult<IReadOnlyList<Session>>([]);
    }

    private static Session S(string id, KeyState state) =>
        new($"ahp-session:/{id}", id, null, state, false, DateTimeOffset.UnixEpoch, []);

    [Fact]
    public void ReportsChangesOnlyWhenVisible()
    {
        var board = new SessionBoard(new NoSource());
        Assert.True(board.Apply([S("a", KeyState.Idle)]));
        Assert.False(board.Apply([S("a", KeyState.Idle)]));
        Assert.True(board.Apply([S("a", KeyState.Working)]));
        Assert.True(board.Apply(null));
        Assert.False(board.Online);
        Assert.Empty(board.Ranked);
    }
}

public class KeyFaceTests
{
    [Fact]
    public void TruncatesLongLabels() => Assert.Equal("Revisar est…", KeyFace.Label("Revisar estado y handoff"));

    [Fact]
    public void BlankForEmpty() => Assert.Equal(KeyFace.Blank, KeyFace.Label("  "));
}

public class ChatUrisTests
{
    private static readonly Guid Id = Guid.Parse("2d523c65-e8e3-409f-8671-e098a2b1d71e");

    [Fact]
    public void OpaqueWhenDefaultChatIsOpaque() =>
        Assert.Equal("ahp-chat:/2d523c65-e8e3-409f-8671-e098a2b1d71e",
            ChatUris.MintPeer("ahp-session:/s", "ahp-chat:/11111111-1111-1111-1111-111111111111", Id));

    [Fact]
    public void ShapedWhenDefaultChatIsShaped() =>
        Assert.Equal("ahp-chat://2d523c65-e8e3-409f-8671-e098a2b1d71e/YWhwLXNlc3Npb246L3M",
            ChatUris.MintPeer("ahp-session:/s", "ahp-chat://default/YWhwLXNlc3Npb246L3M", Id));

    [Fact]
    public void ShapedWhenNoDefaultChat() =>
        Assert.StartsWith("ahp-chat://", ChatUris.MintPeer("copilotcli:/x", null, Id));

    [Fact]
    public void MintedChatIdRoundTrips() =>
        Assert.Equal(Id.ToString(), Links.ChatId(ChatUris.MintPeer("ahp-session:/s", "ahp-chat:/11111111-1111-1111-1111-111111111111", Id), null));
}
