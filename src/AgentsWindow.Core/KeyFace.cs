using System.Text;

namespace AgentsWindow;

public readonly record struct Rgb(byte R, byte G, byte B);

// What a key looks like, independent of the Logi drawing API.
public static class KeyFace
{
    private const int MaxLabelChars = 12;

    // The Plugin Service shows the action's display name when the label is empty, so an
    // unused slot answers with a zero-width space.
    public const string Blank = "\u200B";

    private static readonly Rgb Black = new(0, 0, 0);
    private static readonly Rgb White = new(255, 255, 255);

    // A Copilot face on a full-key background. Active states color the whole key; idle is
    // a white face on black, offline a grey one, and an empty slot a barely visible one.
    // Working uses bright amber with a black face, the only way amber keeps its contrast.
    public static (Rgb Background, Rgb Face) For(KeyState state) => state switch
    {
        KeyState.InputNeeded => (new Rgb(200, 30, 30), White),
        KeyState.Error => (new Rgb(150, 40, 160), White),
        KeyState.Working => (new Rgb(240, 180, 0), Black),
        KeyState.Unread => (new Rgb(40, 100, 200), White),
        KeyState.Idle => (Black, White),
        KeyState.Offline => (Black, new Rgb(110, 110, 110)),
        _ => (Black, new Rgb(45, 45, 45)),
    };

    // Truncates by code point so emoji survive.
    public static string Label(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Blank;
        var runes = text.Trim().EnumerateRunes().ToList();
        if (runes.Count <= MaxLabelChars) return text.Trim();
        var builder = new StringBuilder();
        foreach (var rune in runes.Take(MaxLabelChars - 1)) builder.Append(rune.ToString());
        return builder.Append('…').ToString();
    }
}
