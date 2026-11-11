using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.Core.Tests;

public sealed class Phase2FinishingTests
{
    [Fact]
    public void ReverseDifference_UsesFrontmostPresentationAndSupportsEmptyResults()
    {
        var back = new RectangleShapeNode { Rect = new(0, 0, 100, 100) }; back.Style.Fill = new SolidColorFill(SKColors.Red);
        var front = new RectangleShapeNode { Rect = new(50, 0, 150, 100) }; front.Style.Fill = new SolidColorFill(SKColors.Blue);
        var service = new PathBooleanService();
        var result = service.CombineMany([back, front], Vectorer.Abstractions.Geometry.BooleanOperationType.ReverseDifference)!;
        Assert.Equal(front.Style.Fill, result.Style.Fill); using var geometry = result.BuildLocalPath();
        Assert.False(geometry.Contains(25, 25)); Assert.True(geometry.Contains(125, 25));
        var empty = service.Combine(back, back, Vectorer.Abstractions.Geometry.BooleanOperationType.Difference);
        Assert.NotNull(empty); using var path = empty.BuildLocalPath(); Assert.True(path.IsEmpty);
    }
    [Fact]
    public void Fitter_RetainsEndpointsAndRefitsASmoothPolylineToCubics()
    {
        var model = new EditablePath(); var contour = new PathContour();
        for (var i = 0; i <= 100; i++)
        { var angle = i * MathF.PI / 200; contour.Anchors.Add(new(new SKPoint(100 * MathF.Cos(angle), 100 * MathF.Sin(angle)))); }
        model.Contours.Add(contour); var original = contour.Anchors.Select(a => a.Position).ToArray();
        BezierPathFitter.Simplify(model, 1);
        Assert.True(contour.Anchors.Count < 20); Assert.Contains(contour.Anchors, a => a.Outgoing is not null);
        Assert.Equal(original[0], contour.Anchors[0].Position); Assert.Equal(original[^1], contour.Anchors[^1].Position);
        var samples = EditablePath.Flatten(contour, 0.05f);
        foreach (var point in samples) Assert.InRange(MathF.Abs(PathAnchor.Length(point) - 100), 0, 1.1f);
    }
    [Fact]
    public void Fitter_PreservesClosedSharpCornersAndSeparateContours()
    {
        var model = EditablePath.Parse("M0 0 L10 0 L20 0 L20 20 L0 20 Z M100 100 L120 100");
        BezierPathFitter.Simplify(model, 0.5f);
        Assert.Equal(2, model.Contours.Count); Assert.True(model.Contours[0].IsClosed); Assert.False(model.Contours[1].IsClosed);
        foreach (var p in new[] { new SKPoint(0, 0), new SKPoint(20, 0), new SKPoint(20, 20), new SKPoint(0, 20) })
            Assert.Contains(model.Contours[0].Anchors, a => a.Position == p);
    }
    [Fact]
    public void Join_HandlesReversedEndpointsTransformsAndKeepsOtherContours()
    {
        var first = new PathShapeNode("M0 0 L10 0 M100 100 L120 100") { Transform = SKMatrix.CreateTranslation(20, 30) };
        var second = new PathShapeNode("M30 0 L10 0") { Transform = SKMatrix.CreateTranslation(20, 30) };
        first.Style.Stroke = new StrokeStyle(SKColors.Red, 8);
        var result = PathEditingService.Join(first, second)!;
        Assert.NotNull(result); Assert.Equal(first.Transform, result.Transform); Assert.Equal(first.Style.Stroke, result.Style.Stroke);
        var model = EditablePath.Parse(result.SvgPathData);
        Assert.Equal(2, model.Contours.Count);
        Assert.Equal(new SKPoint(0, 0), model.Contours[0].Anchors[0].Position);
        Assert.Equal(new SKPoint(30, 0), model.Contours[0].Anchors[^1].Position);
        Assert.NotEqual(first.Id, result.Id);
    }
    [Fact]
    public void Join_RejectsClosedPathsWithoutChangingSources()
    {
        var first = new PathShapeNode("M0 0 L10 0 L10 10 Z"); var second = new PathShapeNode("M20 0 L30 0");
        Assert.Null(PathEditingService.Join(first, second));
        Assert.Equal("M0 0 L10 0 L10 10 Z", first.SvgPathData);
    }
    [Fact]
    public void Clone_DeepCopiesHierarchyIdsDashArraysAndShaderUniforms()
    {
        var group = new GroupNode(); var shape = PolygonShapeNode.CreateConcaveStar(new(10, 20), 30, 10, 5);
        shape.Style.Stroke = new StrokeStyle(SKColors.Red, DashArray: [5, 2]);
        shape.ShaderEffect = new ShaderEffectDescriptor("test", Uniforms: new Dictionary<string, float> { ["time"] = 1 });
        group.Children.Add(shape);
        var clone = Assert.IsType<GroupNode>(SceneNodeCloner.Clone(group));
        var child = Assert.IsType<PolygonShapeNode>(clone.Children[0]);
        Assert.NotEqual(group.Id, clone.Id); Assert.NotEqual(shape.Id, child.Id);
        child.Points[0] = new(900, 900); child.Style.Stroke!.DashArray![0] = 99;
        Assert.NotEqual(shape.Points[0], child.Points[0]); Assert.Equal(5, shape.Style.Stroke.DashArray![0]);
        Assert.NotSame(shape.ShaderEffect.Uniforms, child.ShaderEffect!.Uniforms);
        Assert.Equal(shape.Id, Assert.IsType<GroupNode>(SceneNodeCloner.Clone(group, true)).Children[0].Id);
    }
}
