namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// A one-level Dynamic Folder: the host's own back key (top left), New session, then one
// key per VS Code shortcut. Fills a Keypad page with the Agents window's layout keys.
public sealed class ShortcutsFolder : PluginDynamicFolder
{
    private const string New = "new";
    private const string ShortcutPrefix = "k:";
    private bool wasOnline;

    public ShortcutsFolder()
    {
        this.DisplayName = "VS Code shortcuts";
        this.Description = "New session and the Agents window's layout shortcuts in one folder";
        this.GroupName = "Shortcuts";
    }

    private AgentsWindowPlugin Owner => (AgentsWindowPlugin)this.Plugin;

    private SessionBoard Board => this.Owner.Board;

    public override bool Load()
    {
        this.Board.Changed += this.OnBoardChanged;
        return true;
    }

    public override bool Unload()
    {
        this.Board.Changed -= this.OnBoardChanged;
        return true;
    }

    public override PluginDynamicFolderNavigation GetNavigationArea(DeviceType deviceType) => PluginDynamicFolderNavigation.None;

    public override string GetButtonDisplayName(PluginImageSize imageSize) => "Shortcuts";

    public override BitmapImage GetButtonImage(PluginImageSize imageSize) => Faces.Icon("layout", imageSize);

    public override IEnumerable<string> GetButtonPressActionNames(DeviceType deviceType)
    {
        // Renamed when VS Code goes offline so the host redraws it as "Start VS Code".
        yield return this.CreateCommandName(this.Board.Online ? New : New + "-offline");
        foreach (var shortcut in Shortcuts.All)
        {
            yield return this.CreateCommandName(ShortcutPrefix + shortcut.Id);
        }
    }

    public override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) =>
        actionParameter.StartsWith(New, StringComparison.Ordinal)
            ? Faces.NewLabel(this.Board.Online)
            : Find(actionParameter)?.Label ?? KeyFace.Blank;

    public override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) =>
        actionParameter.StartsWith(New, StringComparison.Ordinal)
            ? Faces.NewGlyph(this.Board.Online, imageSize)
            : Faces.Icon(Find(actionParameter)?.Icon ?? "", imageSize);

    public override void RunCommand(string actionParameter)
    {
        if (actionParameter.StartsWith(New, StringComparison.Ordinal))
        {
            Opener.Open(Links.NewSession(" "));
        }
        else if (Find(actionParameter) is { } shortcut)
        {
            _ = this.SendAsync(shortcut);
        }
    }

    private static Shortcut? Find(string actionParameter) =>
        actionParameter.StartsWith(ShortcutPrefix, StringComparison.Ordinal)
            ? Shortcuts.All.FirstOrDefault(s => s.Id == actionParameter[ShortcutPrefix.Length..])
            : null;

    private async Task SendAsync(Shortcut shortcut)
    {
        try
        {
            await this.Owner.Shortcuts.SendAsync(shortcut, CancellationToken.None);
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error($"could not send {shortcut.Id}: {e.Message}");
        }
    }

    private void OnBoardChanged()
    {
        if (this.Board.Online == this.wasOnline) return;
        this.wasOnline = this.Board.Online;
        try
        {
            this.ButtonActionNamesChanged();
        }
        catch (Exception e)
        {
            this.Plugin.Log.Error(e, "could not refresh the shortcuts folder");
        }
    }
}
