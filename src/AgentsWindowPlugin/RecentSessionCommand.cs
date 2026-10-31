namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// "Recent session N": the Nth session by priority, dragged onto a key in Options+.
// The name stays fixed (agentswindow_recent_N) because profiles refer to keys by it.
public sealed class RecentSessionCommand : PluginDynamicCommand
{
    public const int SlotCount = 9;
    private readonly SessionBoard board;
    private readonly int slot;

    public RecentSessionCommand(SessionBoard board, int slot)
        : base($"Recent session {slot}", $"Status of the {Ordinal(slot)} session in the Agents window, active ones first; press to open it", "Recent sessions", DeviceType.All)
    {
        this.board = board;
        this.slot = slot;
        this.Name = $"agentswindow_recent_{slot}";
    }

    public void Redraw()
    {
        try
        {
            this.ActionImageChanged();
        }
        catch (Exception e)
        {
            this.Log.Error(e, $"could not redraw '{this.Name}'");
        }
    }

    protected override void RunCommand(string actionParameter)
    {
        if (this.board.At(this.slot - 1) is { } session)
        {
            Opener.Open(Links.OpenSession(session.Resource));
        }
    }

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize)
    {
        var state = !this.board.Online ? KeyState.Offline : this.board.At(this.slot - 1)?.State ?? KeyState.None;
        return Faces.Ring(state, imageSize);
    }

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
        KeyFace.Label(this.board.Online ? this.board.At(this.slot - 1)?.Title : "offline");

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };
}
