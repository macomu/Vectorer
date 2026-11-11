using SkiaSharp;

namespace Vectorer.Abstractions.Documents;

/// <summary>Editable text; rendering and path conversion share the same glyph outlines.</summary>
public sealed class TextShapeNode : ShapeNode
{
    public string Text { get; set; } = "";
    public SKPoint Position { get; set; }
    public string FontFamily { get; set; } = "sans-serif";
    public float FontSize { get; set; } = 32;
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public SKTextAlign TextAlign { get; set; }
    public float LetterSpacing { get; set; }

    public TextShapeNode() => Name = "Text";

    public override SKPath BuildLocalPath()
    {
        using var typeface = SKTypeface.FromFamilyName(FontFamily,
            IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        using var paint = new SKPaint { Typeface = typeface, TextSize = FontSize, IsAntialias = true };
        var result = new SKPath();
        var y = Position.Y;
        foreach (var line in Text.Replace("\r", "").Split('\n'))
        {
            if (LetterSpacing == 0)
            {
                paint.TextAlign = TextAlign;
                using var glyphs = paint.GetTextPath(line, Position.X, y);
                result.AddPath(glyphs);
            }
            else
            {
                // Enumerate Unicode scalars so surrogate pairs remain a single character.
                var characters = line.EnumerateRunes().Select(r => r.ToString()).ToArray();
                var width = characters.Sum(paint.MeasureText) + Math.Max(0, characters.Length - 1) * LetterSpacing;
                var x = Position.X - (TextAlign == SKTextAlign.Center ? width / 2 : TextAlign == SKTextAlign.Right ? width : 0);
                paint.TextAlign = SKTextAlign.Left;
                foreach (var character in characters)
                {
                    using var glyph = paint.GetTextPath(character, x, y);
                    result.AddPath(glyph); x += paint.MeasureText(character) + LetterSpacing;
                }
            }
            y += FontSize * 1.2f;
        }
        return result;
    }
}
