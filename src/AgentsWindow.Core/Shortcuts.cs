namespace AgentsWindow;

[Flags]
public enum Mods
{
    None = 0,
    Command = 1,
    Option = 2,
    Control = 4,
    Shift = 8,
}

// A VS Code keybinding sent from a key. Key is a letter A–Z. Mods are the macOS
// modifiers; ForWindows gives the Windows binding (Cmd → Ctrl; Option is Alt there).
// Icon names a codicon embedded in the plugin.
public sealed record Shortcut(string Id, string Label, char Key, Mods Mods, string Icon)
{
    public Mods ForWindows => Mods.HasFlag(Mods.Command)
        ? (Mods & ~Mods.Command) | Mods.Control
        : Mods;

    // As macOS menus write it: ⌃⌥⇧⌘ then the key.
    public string Glyph =>
        (Mods.HasFlag(Mods.Control) ? "⌃" : "")
        + (Mods.HasFlag(Mods.Option) ? "⌥" : "")
        + (Mods.HasFlag(Mods.Shift) ? "⇧" : "")
        + (Mods.HasFlag(Mods.Command) ? "⌘" : "")
        + char.ToUpperInvariant(Key);

    // As Windows menus write it: Ctrl+Alt+Shift+B.
    public string WindowsText =>
        string.Join("+", new[]
        {
            ForWindows.HasFlag(Mods.Control) ? "Ctrl" : null,
            ForWindows.HasFlag(Mods.Option) ? "Alt" : null,
            ForWindows.HasFlag(Mods.Shift) ? "Shift" : null,
            char.ToUpperInvariant(Key).ToString(),
        }.Where(part => part is not null));

    public string Text(bool windows) => windows ? WindowsText : Glyph;
}

// Bindings as VS Code 1.144 registers them in the Agents window
// (sessions.desktop.main.js). Changes is the only one with its own macOS binding;
// Browser is Ctrl+Shift+B on Windows too, and in the Agents window it wins over
// Run Build Task.
public static class Shortcuts
{
    // Ids name the actions in profiles; do not rename them.
    public static readonly IReadOnlyList<Shortcut> All =
    [
        new("primary_sidebar", "Sidebar", 'B', Mods.Command, "layout-sidebar-left"),
        new("secondary_sidebar", "Right Sidebar", 'B', Mods.Command | Mods.Option, "layout-sidebar-right"),
        new("terminal", "Terminal", 'J', Mods.Command, "terminal"),
        new("files", "Files", 'E', Mods.Command | Mods.Shift, "files"),
        new("changes", "Changes", 'G', Mods.Control | Mods.Shift, "diff-multiple"),
        new("search", "Search", 'F', Mods.Command | Mods.Shift, "search"),
        new("browser", "Browser", 'B', Mods.Command | Mods.Shift, "globe"),
    ];
}

// Keys go to the frontmost app, so VS Code is brought to the front first and the
// shortcut is only sent once it is there.
public sealed class ShortcutSender(Func<bool> vsCodeFrontmost, Action activateVsCode, Action<Shortcut> send)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PollStep = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

    public async Task<bool> SendAsync(Shortcut shortcut, CancellationToken cancel)
    {
        if (!vsCodeFrontmost())
        {
            activateVsCode();
            var deadline = DateTime.UtcNow + Timeout;
            while (!vsCodeFrontmost())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    Log.Warning($"VS Code did not come to the front in time; not sending {shortcut.Id}");
                    return false;
                }
                await Task.Delay(PollStep, cancel);
            }
            await Task.Delay(Settle, cancel);
        }
        send(shortcut);
        return true;
    }
}
