using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Svg;
using Xunit;

namespace Vectorer.Svg.Tests;

public sealed class EditingRoundTripTests
{
    [Fact]
    public void RoundTrip_PreservesNodeKindsEvenOddAndStandardColorOpacity()
    {
        var doc = SvgDocument.CreateDefault();
        var path = new PathShapeNode("M0 0 L50 0 L50 50 Z") { NodeKinds = "Corner,Smooth,Symmetric", FillType = SKPathFillType.EvenOdd };
        path.Style.Fill = new SolidColorFill(new SKColor(10, 20, 30, 128)); doc.Layers[0].Children.Add(path);
        var serializer = new SvgSerializer(); var xml = serializer.SerializeToString(doc);
        Assert.Contains("fill=\"#0A141E\"", xml); Assert.Contains("fill-opacity", xml);
        var restored = Assert.IsType<PathShapeNode>(serializer.DeserializeFromString(xml).Layers[0].Children[0]);
        Assert.Equal(path.NodeKinds, restored.NodeKinds); Assert.Equal(path.FillType, restored.FillType);
        Assert.Equal(path.Style.Fill, restored.Style.Fill);
    }
    [Fact]
    public void RoundTrip_PreservesStarParametersTransformsAndStrokeDetails()
    {
        var doc = SvgDocument.CreateDefault();
        doc.Layers[0].Transform = SKMatrix.CreateTranslation(15, 20);
        var star = PolygonShapeNode.CreateConcaveStar(new(100, 120), 80, 35, 7, -30);
        star.Transform = SKMatrix.CreateScale(2, 3);
        star.Style.Stroke = new StrokeStyle(SKColors.Blue, 5, SKStrokeCap.Square, SKStrokeJoin.Bevel, 8, [8, 4, 1, 4], 3);
        doc.Layers[0].Children.Add(star);
        var serializer = new SvgSerializer(); var xml = serializer.SerializeToString(doc);
        var result = serializer.DeserializeFromString(xml);
        var restored = Assert.IsType<PolygonShapeNode>(result.Layers[0].Children[0]);
        Assert.Equal(PolygonKind.ConcaveStar, restored.PolygonKind); Assert.Equal(7, restored.PointCount);
        Assert.Equal(star.Center, restored.Center); Assert.Equal(35, restored.InnerRadius);
        Assert.Equal(star.Transform, restored.Transform); Assert.Equal(doc.Layers[0].Transform, result.Layers[0].Transform);
        Assert.Equal(star.Points, restored.Points);
        Assert.Equal(SKStrokeCap.Square, restored.Style.Stroke!.Cap);
        Assert.Equal(SKStrokeJoin.Bevel, restored.Style.Stroke.Join);
        Assert.Equal(star.Style.Stroke.DashArray, restored.Style.Stroke.DashArray);
        Assert.Equal(3, restored.Style.Stroke.DashPhase);
        restored.PointCount = 5; restored.RegeneratePoints(); Assert.Equal(10, restored.Points.Count);
    }
    [Fact]
    public void RoundTrip_PreservesMultilineTextAndTypography()
    {
        var doc = SvgDocument.CreateDefault();
        var text = new TextShapeNode { Text = "Hello & SVG\n日本語", Position = new(20, 80), FontFamily = "Arial", FontSize = 24,
            IsBold = true, IsItalic = true, LetterSpacing = 2, TextAlign = SKTextAlign.Center };
        doc.Layers[0].Children.Add(text);
        var serializer = new SvgSerializer(); var xml = serializer.SerializeToString(doc);
        Assert.Contains("<text", xml); Assert.Contains("<tspan", xml);
        var restored = Assert.IsType<TextShapeNode>(serializer.DeserializeFromString(xml).Layers[0].Children[0]);
        Assert.Equal(text.Text, restored.Text); Assert.Equal(text.Position, restored.Position);
        Assert.True(restored.IsBold); Assert.True(restored.IsItalic);
        Assert.Equal(text.TextAlign, restored.TextAlign); Assert.Equal(2, restored.LetterSpacing);
    }
    [Fact]
    public void Parse_UsesSvgTransformOrderAndLeavesOrdinaryPolygonsFreeform()
    {
        var doc = new SvgSerializer().DeserializeFromString("<svg xmlns='http://www.w3.org/2000/svg'><polygon points='0,0 10,0 10,10' transform='translate(10,20) scale(2)'/></svg>");
        var polygon = Assert.IsType<PolygonShapeNode>(doc.Layers[0].Children[0]);
        Assert.Equal(PolygonKind.Freeform, polygon.PolygonKind);
        Assert.Equal(new SKPoint(12, 22), polygon.Transform.MapPoint(new SKPoint(1, 1)));
    }
}
