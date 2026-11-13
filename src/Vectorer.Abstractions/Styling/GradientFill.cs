using SkiaSharp;

namespace Vectorer.Abstractions.Styling;

/// <summary>A color and its position (0–1) along a gradient.</summary>
public sealed record GradientStop(float Offset, SKColor Color);

public enum GradientSpreadMethod { Pad, Repeat, Reflect }

/// <summary>
/// An SVG linear gradient. Coordinates are fractions of the un-stroked local geometry bounds
/// when <see cref="UseObjectBoundingBox"/> is true, otherwise they are local user coordinates.
/// Treat Stops as immutable: replace the collection when editing to preserve undo snapshots.
/// </summary>
public sealed record LinearGradientFill(
    IReadOnlyList<GradientStop> Stops,
    SKPoint Start,
    SKPoint End,
    bool UseObjectBoundingBox = true,
    SKMatrix? GradientTransform = null,
    GradientSpreadMethod SpreadMethod = GradientSpreadMethod.Pad,
    float Opacity = 1f) : ShapeFill;

/// <summary>A document palette entry. Applying it copies its color, without linking the style.</summary>
public sealed record ColorPreset(SKColor Color, string? Name = null);

public static class PaintVisibility
{
    public static bool IsVisible(ShapeFill? paint) => paint switch
    {
        SolidColorFill solid => solid.Color.Alpha > 0,
        LinearGradientFill gradient => gradient.Opacity > 0 && gradient.Stops.Any(s => s.Color.Alpha > 0),
        TextureFill texture => texture.PrimaryColor.Alpha > 0 || texture.SecondaryColor.Alpha > 0,
        _ => false
    };
}
