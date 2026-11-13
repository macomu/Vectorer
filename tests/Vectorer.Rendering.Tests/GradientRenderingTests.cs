using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Geometry;
using Vectorer.Rendering.Export;
using Xunit;

namespace Vectorer.Rendering.Tests;

public sealed class GradientRenderingTests
{
    private static LinearGradientFill Gradient(float opacity = 1f) => new(
        [new(0, SKColors.Red), new(0.5f, SKColors.Lime), new(1, SKColors.Blue)],
        new(0, 0), new(1, 0), Opacity: opacity);

    private static SKBitmap Render(ShapeNode shape)
    {
        var bitmap = new SKBitmap(260, 160);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        new SkiaDocumentRenderer().RenderNode(shape, canvas);
        return bitmap;
    }

    [Fact]
    public void Fill_RendersArbitraryStopsAndCombinesStopAndOverallAlpha()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Stroke = null;
        shape.Style.Fill = Gradient();
        using var bitmap = Render(shape);
        Assert.True(bitmap.GetPixel(25, 50).Red > 230);
        Assert.True(bitmap.GetPixel(120, 50).Green > 250);
        Assert.True(bitmap.GetPixel(215, 50).Blue > 230);

        shape.Style.Fill = Gradient(0.5f) with
        {
            Stops = [new(0, SKColors.Red.WithAlpha(128)), new(1, SKColors.Red.WithAlpha(128))]
        };
        using var translucent = Render(shape);
        Assert.InRange(translucent.GetPixel(120, 50).Alpha, 63, 65);
    }

    [Fact]
    public void BoundingBoxGradient_UsesNonUniformGradientCoordinateTransform()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Stroke = null;
        shape.Style.Fill = new LinearGradientFill([new(0, SKColors.Red), new(1, SKColors.Blue)], new(0, 0), new(1, 1));
        using var bitmap = Render(shape);
        var a = bitmap.GetPixel(200, 30);
        var b = bitmap.GetPixel(40, 110);
        Assert.InRange(a.Red, 124, 130);
        Assert.InRange(a.Blue, 125, 131);
        Assert.InRange(Math.Abs(a.Red - b.Red), 0, 2);
    }

    [Fact]
    public void Stroke_RendersGradientEvenWhenFallbackColorIsTransparent()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Fill = null;
        shape.Style.Stroke = new StrokeStyle(SKColors.Transparent, 10, Paint: Gradient());
        Assert.True(shape.Style.Stroke.IsVisible);
        using var bitmap = Render(shape);
        Assert.True(bitmap.GetPixel(25, 20).Red > 230);
        Assert.True(bitmap.GetPixel(120, 20).Green > 250);
        Assert.True(bitmap.GetPixel(215, 20).Blue > 230);
        Assert.Equal(0, bitmap.GetPixel(120, 70).Alpha);
    }

    [Fact]
    public void UserSpaceGradient_AppliesTransformAndRepeatSpread()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 200, 100) };
        shape.Style.Stroke = null;
        shape.Style.Fill = new LinearGradientFill([new(0, SKColors.Red), new(1, SKColors.Blue)],
            new(0, 0), new(20, 0), UseObjectBoundingBox: false,
            GradientTransform: SKMatrix.CreateTranslation(10, 0), SpreadMethod: GradientSpreadMethod.Repeat);
        using var bitmap = Render(shape);
        Assert.Equal(bitmap.GetPixel(12, 50), bitmap.GetPixel(32, 50));
        Assert.True(bitmap.GetPixel(12, 50).Red > bitmap.GetPixel(12, 50).Blue);
        Assert.True(bitmap.GetPixel(28, 50).Blue > bitmap.GetPixel(28, 50).Red);
    }

    [Fact]
    public void GradientSelection_ExportsItsPaintedBoundsAndPixels()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Fill = Gradient();
        shape.Style.Stroke = null;
        var export = new PngExportService(new SkiaDocumentRenderer());
        var bytes = export.ExportSelectionToBytes([shape], new(Padding: 0));
        Assert.NotEmpty(bytes);
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(200, bitmap.Width);
        Assert.Equal(100, bitmap.Height);
        Assert.True(bitmap.GetPixel(100, 50).Green > 250);
        shape.Style.Fill = Gradient(0);
        Assert.Empty(export.ExportSelectionToBytes([shape], new(Padding: 0)));
    }

    [Fact]
    public void StrokeToPath_PreservesGradientInOriginalCoordinateSystem()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Fill = null;
        shape.Style.Stroke = new StrokeStyle(SKColors.Transparent, 20, Paint: Gradient());
        var converted = ShapePathConverter.Convert(shape, strokeOnly: true)!;
        using var before = Render(shape);
        using var after = Render(converted);
        foreach (var x in new[] { 20, 60, 120, 180, 220 })
            Assert.Equal(before.GetPixel(x, 20), after.GetPixel(x, 20));
    }

    [Fact]
    public void EmptySingleAndCoincidentGradients_HaveDefinedRendering()
    {
        var shape = new RectangleShapeNode { Rect = new(20, 20, 220, 120) };
        shape.Style.Stroke = null;
        shape.Style.Fill = Gradient() with { Stops = [] };
        using var empty = Render(shape);
        Assert.Equal(0, empty.GetPixel(120, 50).Alpha);
        shape.Style.Fill = Gradient() with { Stops = [new(0.3f, SKColors.Gold)] };
        using var single = Render(shape);
        Assert.Equal(SKColors.Gold, single.GetPixel(120, 50));
        shape.Style.Fill = Gradient() with { End = new(0, 0) };
        using var coincident = Render(shape);
        Assert.Equal(SKColors.Blue, coincident.GetPixel(120, 50));
    }
}
