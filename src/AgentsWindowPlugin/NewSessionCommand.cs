namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// Opens the Agents window on the new-session composer. VS Code requires a prompt in the
// link and never submits it, so the composer opens with this text as a draft.
public sealed class NewSessionCommand : PluginDynamicCommand
{
    private const string DraftPrompt = " ";

    public NewSessionCommand()
        : base("New session", "Open a new session in the VS Code Agents window", "Agents window", DeviceType.All)
    {
        this.Name = "agentswindow_new_session";
    }

    protected override void RunCommand(string actionParameter) => Opener.Open(Links.NewSession(DraftPrompt));

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => Faces.Glyph("+", imageSize);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => "New";
}
