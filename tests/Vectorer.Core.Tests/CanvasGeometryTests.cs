using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.Core.Tests;

public sealed class CanvasGeometryTests
{
    [Fact]
    public void DragBounds_NormalizesReverseDragsAndSupportsSquareFromCenter()
    {
        Assert.Equal(new SKRect(10, 20, 100, 80), CanvasGeometry.DragBounds(new(100, 80), new(10, 20), false, false));
        Assert.Equal(new SKRect(60, 60, 140, 140), CanvasGeometry.DragBounds(new(100, 100), new(120, 60), true, true));
    }

    [Fact]
    public void CreateShape_PreservesToolParameters()
    {
        var rect = Assert.IsType<RectangleShapeNode>(CanvasGeometry.CreateShape(DrawingTool.Rectangle, new(10, 20), new(80, 90), cornerRadius: 12));
        Assert.Equal(12f, rect.CornerRadiusX);
        var arc = Assert.IsType<ArcShapeNode>(CanvasGeometry.CreateShape(DrawingTool.Arc, new(0, 0), new(100, 60),
            startAngle: 30, sweepAngle: 120, closeMode: ArcCloseMode.Open));
        Assert.Equal(new SKPoint(50, 30), arc.Center);
        Assert.Equal(120f, arc.SweepAngleDegrees);
        Assert.Equal(ArcCloseMode.Open, arc.CloseMode);
        var star = Assert.IsType<PolygonShapeNode>(CanvasGeometry.CreateShape(DrawingTool.Star, new(50, 50), new(100, 50), sides: 7, innerRatio: 0.5f));
        Assert.Equal(14, star.Points.Count);
        Assert.Equal(new SKPoint(100, 50), star.Points[0]);
        Assert.InRange(MainDistance(star.Points[1], new(50, 50)), 24.99f, 25.01f);
    }

    [Fact]
    public void Snap_UsesDocumentCoordinatesIncludingNegativeValues()
    {
        Assert.Equal(new SKPoint(40, -20), CanvasGeometry.Snap(new(31, -29)));
    }

    [Fact]
    public void HitTest_UsesShapeGeometryAndAccumulatedTransforms()
    {
        var ellipse = new EllipseShapeNode { Center = new(20, 20), RadiusX = 20, RadiusY = 20,
            Transform = SKMatrix.CreateTranslation(10, 0) };
        ellipse.Style.Stroke = null;
        var parent = SKMatrix.CreateScale(2, 2);
        Assert.True(CanvasGeometry.HitTest(ellipse, parent, new(60, 40), 1));
        Assert.False(CanvasGeometry.HitTest(ellipse, parent, new(21, 1), 1));
        using var path = CanvasGeometry.BuildWorldPath(ellipse, parent);
        Assert.Equal(new SKRect(20, 0, 100, 80), path.TightBounds);
    }

    [Fact]
    public void HitTest_SelectsOpenPathsAndDoesNotFillUnpaintedInteriors()
    {
        var path = new PathShapeNode("M 0 0 L 100 0") { Style = new ShapeStyle { Fill = null, Stroke = new StrokeStyle(SKColors.Black, 4) } };
        Assert.True(CanvasGeometry.HitTest(path, SKMatrix.CreateIdentity(), new(50, 2), 3));
        Assert.False(CanvasGeometry.HitTest(path, SKMatrix.CreateIdentity(), new(50, 20), 3));
        var rectangle = new RectangleShapeNode { Rect = new(0, 0, 100, 100), Style = new ShapeStyle { Fill = null, Stroke = new StrokeStyle(SKColors.Black, 2) } };
        Assert.False(CanvasGeometry.HitTest(rectangle, SKMatrix.CreateIdentity(), new(50, 50), 3));
    }

    [Fact]
    public void EditableNodes_ExcludesLockedHiddenAndSingularLayers()
    {
        var doc = SvgDocument.CreateDefault();
        var available = new RectangleShapeNode();
        doc.Layers[0].Children.AddRange([available, new RectangleShapeNode { IsLocked = true }, new RectangleShapeNode { IsVisible = false }]);
        foreach (var layer in new[] { new LayerNode { IsLocked = true }, new LayerNode { IsVisible = false }, new LayerNode { Transform = SKMatrix.CreateScale(0, 1) } })
        {
            layer.Children.Add(new RectangleShapeNode());
            doc.Layers.Add(layer);
        }
        Assert.Same(available, Assert.Single(CanvasGeometry.EditableNodes(doc)).Node);
    }

    [Fact]
    public void ApplyWorldTransform_TranslatesCorrectlyInsideRotatedScaledLayer()
    {
        var parent = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(90), SKMatrix.CreateScale(2, 3));
        var original = SKMatrix.CreateTranslation(10, 20);
        var delta = SKMatrix.CreateTranslation(30, -15);
        var result = CanvasGeometry.ApplyWorldTransform(original, parent, delta);
        var before = SKMatrix.Concat(parent, original).MapPoint(new SKPoint(5, 7));
        var after = SKMatrix.Concat(parent, result).MapPoint(new SKPoint(5, 7));
        Assert.InRange(after.X - before.X, 29.999f, 30.001f);
        Assert.InRange(after.Y - before.Y, -15.001f, -14.999f);
    }

    [Fact]
    public void BuildWorldPath_CombinesNestedGroupTransforms()
    {
        var group = new GroupNode { Transform = SKMatrix.CreateTranslation(20, 30) };
        group.Children.Add(new RectangleShapeNode { Rect = new(0, 0, 10, 10), Transform = SKMatrix.CreateTranslation(5, 7) });
        using var path = CanvasGeometry.BuildWorldPath(group, SKMatrix.CreateScale(2, 2));
        Assert.Equal(new SKRect(50, 74, 70, 94), path.TightBounds);
    }

    private static float MainDistance(SKPoint a, SKPoint b) => MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
}
