namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// Opens the Agents window on the new-session composer. VS Code requires a prompt in the
// link and never submits it, so the composer opens with this text as a draft.
// Shows "Start VS Code" while VS Code is not running; the same link starts it.
public sealed class NewSessionCommand : PluginDynamicCommand
{
    private const string DraftPrompt = " ";
    private readonly SessionBoard board;

    public NewSessionCommand(SessionBoard board)
        : base("New session", "Open a new session in the VS Code Agents window, starting VS Code if needed", "Agents window", DeviceType.All)
    {
        this.board = board;
        this.Name = "agentswindow_new_session";
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

    protected override void RunCommand(string actionParameter) => Opener.Open(Links.NewSession(DraftPrompt));

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => Faces.NewGlyph(this.board.Online, imageSize);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => Faces.NewLabel(this.board.Online);
}
