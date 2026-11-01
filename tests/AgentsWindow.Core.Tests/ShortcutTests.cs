using AgentsWindow;

namespace AgentsWindow.Tests;

public class ShortcutTests
{
    [Fact]
    public void DrawsGlyphsInMacOrder()
    {
        Assert.Equal(["⌘B", "⌥⌘B", "⌘J", "⇧⌘E", "⌃⇧G", "⇧⌘B"], Shortcuts.All.Select(s => s.Glyph));
    }

    [Fact]
    public void WritesWindowsShortcuts()
    {
        Assert.Equal(["Ctrl+B", "Ctrl+Alt+B", "Ctrl+J", "Ctrl+Shift+E", "Ctrl+Shift+G", "Ctrl+Shift+B"], Shortcuts.All.Select(s => s.WindowsText));
    }

    [Fact]
    public void IdsAreUniqueLetters()
    {
        Assert.Equal(Shortcuts.All.Count, Shortcuts.All.Select(s => s.Id).Distinct().Count());
        Assert.All(Shortcuts.All, s => Assert.InRange(s.Key, 'A', 'Z'));
    }

    [Fact]
    public void WindowsSwapsCommandForControl()
    {
        Assert.Equal(Mods.Control | Mods.Option, new Shortcut("x", "x", 'B', Mods.Command | Mods.Option, "i").ForWindows);
        Assert.Equal(Mods.Control | Mods.Shift, new Shortcut("x", "x", 'G', Mods.Control | Mods.Shift, "i").ForWindows);
    }

    [Fact]
    public async Task SendsRightAwayWhenFrontmost()
    {
        var sent = new List<string>();
        var activated = 0;
        var sender = new ShortcutSender(() => true, () => activated++, s => sent.Add(s.Id));
        Assert.True(await sender.SendAsync(Shortcuts.All[0], CancellationToken.None));
        Assert.Equal(["primary_sidebar"], sent);
        Assert.Equal(0, activated);
    }

    [Fact]
    public async Task ActivatesThenSends()
    {
        var front = false;
        var sent = new List<string>();
        var sender = new ShortcutSender(() => front, () => front = true, s => sent.Add(s.Id));
        Assert.True(await sender.SendAsync(Shortcuts.All[2], CancellationToken.None));
        Assert.Equal(["terminal"], sent);
    }

    [Fact]
    public async Task DoesNotSendWhenVsCodeStaysBehind()
    {
        var sent = new List<string>();
        var sender = new ShortcutSender(() => false, () => { }, s => sent.Add(s.Id));
        Assert.False(await sender.SendAsync(Shortcuts.All[0], CancellationToken.None));
        Assert.Empty(sent);
    }
}
