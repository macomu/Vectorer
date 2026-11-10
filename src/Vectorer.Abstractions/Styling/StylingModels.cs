using SkiaSharp;

namespace Vectorer.Abstractions.Styling;

/// <summary>
/// Supported blend modes for layer and node compositing.
/// </summary>
public enum LayerBlendMode
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Add,
    Subtract,
    Difference,
    Darken,
    Lighten
}

/// <summary>
/// Built-in procedural texture patterns for texture fills.
/// </summary>
public enum TexturePatternKind
{
    Checkerboard,
    DiagonalStripes,
    Crosshatch,
    Dots,
    CustomImage
}

/// <summary>
/// Base class for shape fill definitions.
/// </summary>
public abstract record ShapeFill;

/// <summary>
/// Solid color fill.
/// </summary>
public sealed record SolidColorFill(SKColor Color) : ShapeFill;

/// <summary>
/// Texture / pattern fill for painting inside shapes.
/// </summary>
public sealed record TextureFill(
    TexturePatternKind PatternKind,
    SKColor PrimaryColor,
    SKColor SecondaryColor,
    float Scale = 16f,
    float AngleDegrees = 0f,
    string? CustomImagePath = null) : ShapeFill;

/// <summary>
/// Outline (stroke) rendering settings.
/// </summary>
public sealed record StrokeStyle(
    SKColor Color,
    float Width = 2f,
    SKStrokeCap Cap = SKStrokeCap.Round,
    SKStrokeJoin Join = SKStrokeJoin.Round,
    float MiterLimit = 4f,
    float[]? DashArray = null,
    float DashPhase = 0f)
{
    public bool IsVisible => Width > 0f && Color.Alpha > 0;
}

/// <summary>
/// Filter settings applied to a node or layer (e.g., Gaussian blur).
/// </summary>
public sealed record FilterSettings(
    float BlurSigmaX = 0f,
    float BlurSigmaY = 0f)
{
    public bool HasActiveFilter => BlurSigmaX > 0f || BlurSigmaY > 0f;
}

/// <summary>
/// Runtime SkiaSharp shader (SkSL) attached to an individual object or layer.
/// </summary>
public sealed record ShaderEffectDescriptor(
    string ShaderId,
    string? InlineSksl = null,
    IReadOnlyDictionary<string, float>? Uniforms = null);

/// <summary>
/// Combined fill and outline style for a vector shape.
/// </summary>
public sealed class ShapeStyle
{
    public ShapeFill? Fill { get; set; } = new SolidColorFill(new SKColor(66, 133, 244));
    public StrokeStyle? Stroke { get; set; } = new StrokeStyle(new SKColor(30, 41, 59), 2f);

    public ShapeStyle Clone() => new()
    {
        Fill = Fill,
        Stroke = Stroke
    };
}
