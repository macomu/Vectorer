using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.Core.Tests;

public sealed class EditingModelsTests
{
    [Fact]
    public void Snap_UsesClosestCandidateOnlyWithinScreenTolerance()
    {
        Assert.Equal(new SKPoint(11, 10), SnapGeometry.Nearest(new(10, 10), [new(15, 10), new(11, 10)], 4));
        Assert.Null(SnapGeometry.Nearest(new(10, 10), [new(30, 30)], 4));
    }
    [Fact]
    public void Flatten_AdaptsToLargeCurvesAndKeepsEndpoints()
    {
        var model = EditablePath.Parse("M0 0 C0 1000 1000 1000 1000 0");
        var coarse = EditablePath.Flatten(model.Contours[0], 20);
        var fine = EditablePath.Flatten(model.Contours[0], 0.1f);
        Assert.True(fine.Count > coarse.Count);
        Assert.Equal(new SKPoint(0, 0), fine[0]); Assert.Equal(new SKPoint(1000, 0), fine[^1]);
        Assert.Contains(fine, p => MathF.Abs(p.X - 500) < 0.001f && MathF.Abs(p.Y - 750) < 0.001f);
    }
    [Fact]
    public void Polygon_RegeneratesParametersAndKeepsFreeformVertices()
    {
        var polygon = PolygonShapeNode.CreateConcaveStar(new(50, 60), 40, 20, 5);
        polygon.PointCount = 7; polygon.RegeneratePoints();
        Assert.Equal(14, polygon.Points.Count);
        polygon.Points[0] = new(1, 2); polygon.ConvertToFreeform(); polygon.RegeneratePoints();
        Assert.Equal(new SKPoint(1, 2), polygon.Points[0]);
    }
    [Fact]
    public void StyleEdit_ChangesOnlyRequestedAttributeAndUndoesEachOriginalStyle()
    {
        var a = new RectangleShapeNode(); var b = new RectangleShapeNode();
        a.Style.Fill = new SolidColorFill(SKColors.Red); b.Style.Fill = new SolidColorFill(SKColors.Blue);
        a.Style.Stroke = new StrokeStyle(SKColors.Green, 2, DashArray: [4, 2]);
        b.Style.Stroke = new StrokeStyle(SKColors.Yellow, 8);
        var undo = new UndoRedoService();
        undo.ExecuteCommand(new StyleEditCommand([a, b], s => s.Stroke = s.Stroke! with { Width = 5 }, "Width"));
        Assert.Equal(new SolidColorFill(SKColors.Red), a.Style.Fill);
        Assert.Equal(new SolidColorFill(SKColors.Blue), b.Style.Fill);
        Assert.Equal(SKColors.Green, a.Style.Stroke!.Color); Assert.Equal(5, b.Style.Stroke!.Width);
        Assert.Equal(new float[] { 4, 2 }, a.Style.Stroke.DashArray);
        undo.Undo(); Assert.Equal(2, a.Style.Stroke.Width); Assert.Equal(8, b.Style.Stroke.Width);
        undo.Redo(); Assert.Equal(5, a.Style.Stroke.Width);
    }
    [Fact]
    public void SplitCubic_PreservesTheOriginalCurve()
    {
        var model = EditablePath.Parse("M0 0 C20 80 80 -20 100 50");
        var contour = model.Contours[0]; var a = contour.Anchors[0].Clone(); var b = contour.Anchors[1].Clone();
        EditablePath.SplitSegment(contour, 0, 0.4f);
        for (var i = 0; i <= 100; i++)
        {
            var t = i / 100f;
            var actual = t <= 0.4f ? EditablePath.Evaluate(contour.Anchors[0], contour.Anchors[1], t / 0.4f)
                : EditablePath.Evaluate(contour.Anchors[1], contour.Anchors[2], (t - 0.4f) / 0.6f);
            Assert.InRange(PathAnchor.Length(actual - EditablePath.Evaluate(a, b, t)), 0, 0.001f);
        }
    }
    [Fact]
    public void Handles_PreserveSymmetryAndSmoothOppositeLength()
    {
        var anchor = new PathAnchor(new(0, 0)) { Incoming = new(-10, 0), Outgoing = new(20, 0), Kind = PathNodeKind.Smooth };
        anchor.MoveHandle(true, new(0, -30));
        Assert.Equal(new SKPoint(0, 20), anchor.Outgoing);
        anchor.Kind = PathNodeKind.Symmetric; anchor.MoveHandle(true, new(-15, -20));
        Assert.Equal(new SKPoint(15, 20), anchor.Outgoing);
        anchor.MoveTo(new(10, 10)); Assert.Equal(new SKPoint(25, 30), anchor.Outgoing);
    }
    [Fact]
    public void EditablePath_PreservesMultipleContoursAndQuadraticGeometry()
    {
        var model = EditablePath.Parse("M0 0 Q50 100 100 0 Z M200 0 L250 50");
        Assert.Equal(2, model.Contours.Count); Assert.True(model.Contours[0].IsClosed);
        Assert.False(model.Contours[1].IsClosed);
        var midpoint = EditablePath.Evaluate(model.Contours[0].Anchors[0], model.Contours[0].Anchors[1], 0.5f);
        Assert.InRange(PathAnchor.Length(midpoint - new SKPoint(50, 50)), 0, 0.001f);
        model.Cut(model.Contours[1], 0); Assert.Equal(2, model.Contours.Count);
    }
    [Fact]
    public void Simplify_ReducesStraightLineNoiseWithinTolerance()
    {
        SKPoint[] points = [new(0, 0), new(10, 0.1f), new(20, -0.1f), new(30, 0), new(30, 20)];
        var simplified = PathShapeNodeBuilder.Simplify(points, 0.5f);
        Assert.Equal(3, simplified.Count); Assert.Equal(points[0], simplified[0]); Assert.Equal(points[^1], simplified[^1]);
        using var path = SKPath.ParseSvgPathData(EditablePath.Builder(points, 50, true).Build());
        Assert.NotNull(path); Assert.False(path.IsEmpty);
    }
    [Fact]
    public void Text_BuildsGlyphsAndConvertsToPathWithoutLosingStyle()
    {
        var text = new TextShapeNode { Text = "Vectorer\nSVG", Position = new(10, 60), FontSize = 36, IsBold = true, LetterSpacing = 2 };
        using var path = text.BuildLocalPath(); Assert.False(path.IsEmpty);
        var converted = ShapePathConverter.Convert(text)!;
        using var outline = converted.BuildLocalPath(); Assert.False(outline.IsEmpty);
        Assert.Equal(text.Style.Fill, converted.Style.Fill); Assert.True(path.Bounds.Height > 36);
    }
}
