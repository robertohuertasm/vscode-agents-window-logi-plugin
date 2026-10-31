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
// modifiers; ForWindows gives the default Windows binding (Cmd → Ctrl, Option → Alt).
public sealed record Shortcut(string Id, string Label, char Key, Mods Mods)
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
}

public static class Shortcuts
{
    // Ids name the actions in profiles; do not rename them.
    public static readonly IReadOnlyList<Shortcut> All =
    [
        new("primary_sidebar", "Left sidebar", 'B', Mods.Command),
        new("secondary_sidebar", "Right sidebar", 'B', Mods.Command | Mods.Option),
        new("terminal", "Terminal", 'J', Mods.Command),
        new("files", "Files", 'E', Mods.Command | Mods.Shift),
        new("changes", "Changes", 'G', Mods.Control | Mods.Shift),
        new("browser", "Browser", 'B', Mods.Command | Mods.Shift),
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
