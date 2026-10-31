namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// One key per VS Code keybinding (toggle sidebars, terminal, files, changes, browser).
// Brings VS Code to the front first, since keys go to the frontmost app.
public sealed class ShortcutCommand : PluginDynamicCommand
{
    private readonly AgentsWindowPlugin owner;
    private readonly Shortcut shortcut;

    public ShortcutCommand(AgentsWindowPlugin owner, Shortcut shortcut)
        : base(shortcut.Label, $"Press {shortcut.Glyph} in VS Code", "Shortcuts", DeviceType.All)
    {
        this.owner = owner;
        this.shortcut = shortcut;
        this.Name = $"agentswindow_shortcut_{shortcut.Id}";
    }

    protected override void RunCommand(string actionParameter) => _ = this.SendAsync();

    private async Task SendAsync()
    {
        try
        {
            await this.owner.Shortcuts.SendAsync(this.shortcut, CancellationToken.None);
        }
        catch (Exception e)
        {
            this.Log.Error($"could not send {this.shortcut.Id}: {e.Message}");
        }
    }

    protected override BitmapImage GetCommandImage(string actionParameter, PluginImageSize imageSize) => Faces.Glyph(this.shortcut.Glyph, imageSize, fontScale: 0.24f);

    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.shortcut.Label;
}
