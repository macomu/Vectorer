using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.Core.Tests;

public sealed class ShapePathConverterTests
{
    [Fact]
    public void ObjectToPath_PreservesGeometryTransformAndPresentation()
    {
        var source = new RectangleShapeNode { Rect = new(0, 0, 100, 80), CornerRadiusX = 10, CornerRadiusY = 10,
            Transform = SKMatrix.CreateRotationDegrees(25), Opacity = 0.6f,
            BlendMode = LayerBlendMode.Multiply, Filter = new FilterSettings(2, 2), ShaderEffect = new ShaderEffectDescriptor("example") };
        source.CustomAttributes["example"] = "retained";
        var converted = ShapePathConverter.Convert(source)!;
        using var expected = source.BuildTransformedPath();
        using var actual = converted.BuildTransformedPath();
        Assert.InRange(MathF.Abs(expected.TightBounds.Left - actual.TightBounds.Left), 0, 0.001f);
        Assert.InRange(MathF.Abs(expected.TightBounds.Right - actual.TightBounds.Right), 0, 0.001f);
        Assert.InRange(MathF.Abs(expected.TightBounds.Top - actual.TightBounds.Top), 0, 0.001f);
        Assert.InRange(MathF.Abs(expected.TightBounds.Bottom - actual.TightBounds.Bottom), 0, 0.001f);
        for (var x = -50; x < 150; x += 3)
            for (var y = -50; y < 150; y += 3)
                Assert.Equal(expected.Contains(x + 0.2f, y + 0.2f), actual.Contains(x + 0.2f, y + 0.2f));
        Assert.Equal(source.Id, converted.Id);
        Assert.Equal(source.Opacity, converted.Opacity);
        Assert.Equal(source.BlendMode, converted.BlendMode);
        Assert.Equal(source.Filter, converted.Filter);
        Assert.Equal(source.ShaderEffect, converted.ShaderEffect);
        Assert.Equal("retained", converted.CustomAttributes["example"]);
        Assert.NotSame(source.Style, converted.Style);
    }

    [Fact]
    public void StrokeToPath_UsesWidthCapsAndStrokeColor()
    {
        var source = new PathShapeNode("M 10 20 L 110 20");
        source.Style.Stroke = new StrokeStyle(SKColors.Red, 10, SKStrokeCap.Square);
        var converted = ShapePathConverter.Convert(source, true)!;
        Assert.Null(converted.Style.Stroke);
        Assert.Equal(new SolidColorFill(SKColors.Red), converted.Style.Fill);
        using var path = converted.BuildLocalPath();
        Assert.Equal(new SKRect(5, 15, 115, 25), path.TightBounds);
    }

    [Fact]
    public void StrokeToPath_PreservesDashGaps()
    {
        var source = new PathShapeNode("M 0 0 L 100 0");
        source.Style.Stroke = new StrokeStyle(SKColors.Blue, 4, SKStrokeCap.Butt, DashArray: [10, 10]);
        using var path = ShapePathConverter.Convert(source, true)!.BuildLocalPath();
        Assert.True(path.Contains(5, 0));
        Assert.False(path.Contains(15, 0));
    }

    [Fact]
    public void StrokeToPath_RejectsInvisibleStroke()
    {
        var source = new RectangleShapeNode();
        source.Style.Stroke = null;
        Assert.Null(ShapePathConverter.Convert(source, true));
    }

    [Fact]
    public void StrokeToPath_RetainsGradientPaintAndOriginalBounds()
    {
        var gradient = new LinearGradientFill([new(0, SKColors.Red), new(1, SKColors.Blue)], new(0, 0), new(1, 0));
        var source = new RectangleShapeNode { Rect = new(10, 20, 110, 70) };
        source.Style.Stroke = new StrokeStyle(SKColors.Transparent, 10, Paint: gradient);
        var converted = ShapePathConverter.Convert(source, true)!;
        var fill = Assert.IsType<LinearGradientFill>(converted.Style.Fill);
        Assert.False(fill.UseObjectBoundingBox);
        Assert.Equal(new SKPoint(10, 20), fill.GradientTransform!.Value.MapPoint(fill.Start));
        Assert.Equal(new SKPoint(110, 20), fill.GradientTransform!.Value.MapPoint(fill.End));
        Assert.Same(gradient.Stops, fill.Stops);
        Assert.Null(converted.Style.Stroke);
    }
}
