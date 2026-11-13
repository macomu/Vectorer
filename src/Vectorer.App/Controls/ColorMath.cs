using Avalonia.Media;

namespace Vectorer.App.Controls;

/// <summary>Color conversions used by the component sliders. Hue is retained for achromatic colors.</summary>
public static class ColorMath
{
    public static (double H, double S, double V) ToHsv(Color color, double previousHue = 0)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var hue = previousHue;
        if (delta > 0)
        {
            hue = max == r ? 60 * ((g - b) / delta % 6)
                : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
        }
        return (hue, max == 0 ? 0 : delta / max, max);
    }

    public static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        hue = (hue % 360 + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation; var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - chroma;
        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d),
            < 180 => (0d, chroma, x), < 240 => (0d, x, chroma),
            < 300 => (x, 0d, chroma), _ => (chroma, 0d, x)
        };
        return Color.FromArgb(alpha, Byte(r + m), Byte(g + m), Byte(b + m));
    }

    public static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}
