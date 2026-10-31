namespace Loupedeck.AgentsWindowPlugin;

using AgentsWindow;

// Draws key faces: a status ring above the label row Options+ reserves.
internal static class Faces
{
    public static BitmapImage Ring(KeyState state, PluginImageSize size)
    {
        var (rgb, style) = KeyFace.For(state);
        var color = new BitmapColor(rgb.R, rgb.G, rgb.B);
        using var b = new BitmapBuilder(size);
        b.Clear(BitmapColor.Black);
        var cx = b.Width / 2f;
        var cy = b.Height * 0.4f;
        var r = Math.Min(b.Width, b.Height) * 0.26f;
        switch (style)
        {
            case RingStyle.Filled:
                b.FillCircle(cx, cy, r, color);
                break;
            case RingStyle.Outline:
                b.DrawArc((int)cx, (int)cy, (int)r, 0, 360, color, Math.Max(2, b.Width / 20f));
                break;
            default:
                for (var a = 0; a < 360; a += 30)
                {
                    b.DrawArc((int)cx, (int)cy, (int)r, a, 12, color, Math.Max(2, b.Width / 24f));
                }
                break;
        }
        return b.ToImage();
    }

    public static BitmapImage Glyph(string text, PluginImageSize size, BitmapColor? background = null)
    {
        using var b = new BitmapBuilder(size);
        b.Clear(background ?? BitmapColor.Black);
        b.DrawText(text, 0, 0, b.Width, (int)(b.Height * 0.8), BitmapColor.White, (int)(b.Height * 0.4), 0, 0, null);
        return b.ToImage();
    }
}
