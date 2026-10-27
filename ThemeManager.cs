using System.Windows;
using System.Windows.Media;

namespace DestNote;

public sealed record PaperPalette(
    string Id,
    string Name,
    string Paper,
    string PaperDeep,
    string Line,
    string Accent,
    string AccentDeep,
    string AccentSoft,
    string Tape)
{
    public static readonly IReadOnlyList<PaperPalette> All = new[]
    {
        new PaperPalette("cream", "奶油", "#FFF9E8", "#F2E8CC", "#DCCDA8", "#4E705B", "#35513F", "#E2EBDD", "#D8C69B"),
        new PaperPalette("matcha", "抹茶", "#F3F7E9", "#E4EBD2", "#CCD7B4", "#547348", "#3C5633", "#E3EBD6", "#C8D5AA"),
        new PaperPalette("sakura", "樱粉", "#FDF1ED", "#F4DED7", "#E7C8BE", "#A05F5B", "#7F4542", "#F5DFDB", "#E5C4B9"),
        new PaperPalette("mist", "雾蓝", "#EFF6F8", "#DFEBF0", "#C4D8DF", "#4D7282", "#35545F", "#DCEBF0", "#BFD3DB")
    };

    public static PaperPalette Get(string? id)
        => All.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

public static class ThemeManager
{
    public static PaperPalette Current { get; private set; } = PaperPalette.All[0];

    public static void Apply(string? id)
    {
        Current = PaperPalette.Get(id);
        var palette = Current;
        SetBrush("PaperBrush", palette.Paper);
        SetBrush("PaperDeepBrush", palette.PaperDeep);
        SetBrush("PaperLineBrush", palette.Line);
        SetBrush("TapeBrush", palette.Tape);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentDeepBrush", palette.AccentDeep);
        SetBrush("AccentSoftBrush", palette.AccentSoft);
        SetBrush("CardBrush", Blend(palette.Paper, "#FFFFFF", 0.55));
        SetBrush("CardStrongBrush", Blend(palette.Paper, "#FFFFFF", 0.82));
        SetBrush("HoverBrush", Blend(palette.Paper, "#000000", 0.055));
        SetBrush("PressedBrush", Blend(palette.Paper, "#000000", 0.10));
        SetBrush("ScrollThumbBrush", Blend(palette.Accent, "#FFFFFF", 0.35, 0.55));
        SetBrush("InkBrush", "#2C332E");
        SetBrush("MutedBrush", "#707970");
        SetBrush("FaintBrush", "#9BA197");
        SetBrush("DangerBrush", "#B85B4E");
        SetBrush("DangerSoftBrush", "#F6E3DF");
        SetBrush("GoldBrush", "#B9873E");
        SetBrush("GoldSoftBrush", "#F6EBCF");
        SetBrush("ScrimBrush", Blend(palette.Paper, "#000000", 0.14, 0.50));
        SetBrush("PaperOverlayBrush", palette.Paper);
    }

    private static void SetBrush(string key, string hex)
    {
        var color = ParseColor(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        System.Windows.Application.Current.Resources[key] = brush;
    }

    private static void SetBrush(string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        System.Windows.Application.Current.Resources[key] = brush;
    }

    public static Color ParseColor(string hex)
    {
        if (hex.StartsWith('#'))
            return (Color)ColorConverter.ConvertFromString(hex);
        return (Color)ColorConverter.ConvertFromString("#" + hex);
    }

    public static Color Blend(string a, string b, double amount, double alpha = 1)
    {
        var ca = ParseColor(a);
        var cb = ParseColor(b);
        return Color.FromArgb(
            (byte)Math.Round(255 * alpha),
            (byte)Math.Round(ca.R + (cb.R - ca.R) * amount),
            (byte)Math.Round(ca.G + (cb.G - ca.G) * amount),
            (byte)Math.Round(ca.B + (cb.B - ca.B) * amount));
    }

    public static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush(ParseColor(hex));
        brush.Freeze();
        return brush;
    }
}
