using Vectorer.Core.Geometry;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Tools;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class ShapeEditingTests
{
    private static MainWindowViewModel Workspace(SceneNode shape, DrawingTool tool)
    {
        var vm = new MainWindowViewModel();
        vm.ActiveLayer!.Children.Clear(); vm.ActiveLayer.Children.Add(shape);
        vm.ChooseTool(tool); vm.SelectNodes([]);
        return vm;
    }

    [Fact]
    public void MatchingTool_SelectsWithoutCreatingOrChangingTheShape()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 100, 60) };
        var vm = Workspace(shape, DrawingTool.Rectangle);
        vm.BeginInteraction(new(40, 30), false); vm.EndInteraction();
        Assert.Same(shape, Assert.Single(vm.SelectedNodes));
        Assert.Single(vm.ActiveLayer!.Children); Assert.False(vm.HasUnsavedChanges);
        Assert.True(vm.ShowRectangleInputs); Assert.False(vm.ShowTransformInputs);
    }

    [Fact]
    public void Resize_PreservesTransformedParentStrokeTextureAndCornerRadiiWithUndoRedo()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 100, 60), CornerRadiusX = 8, CornerRadiusY = 6,
            Transform = SKMatrix.CreateRotationDegrees(30) };
        shape.Style.Stroke = new StrokeStyle(SKColors.Black, 4);
        shape.Style.Fill = new TextureFill(TexturePatternKind.Checkerboard, SKColors.Red, SKColors.Blue, 16);
        var vm = Workspace(shape, DrawingTool.Rectangle);
        vm.ActiveLayer!.Transform = SKMatrix.CreateTranslation(200, 100);
        vm.SelectNodes([shape]);
        var matrix = shape.Transform; var world = SKMatrix.Concat(vm.ActiveLayer.Transform, matrix);
        var style = shape.Style.Clone();
        vm.BeginInteraction(world.MapPoint(new(100, 60)), false);
        vm.UpdateInteraction(world.MapPoint(new(180, 90)), false, false); vm.EndInteraction();
        Assert.Equal(180, shape.Rect.Width, 3); Assert.Equal(90, shape.Rect.Height, 3);
        Assert.Equal(matrix, shape.Transform); Assert.Equal(style.Fill, shape.Style.Fill);
        Assert.Equal(4, shape.Style.Stroke!.Width); Assert.Equal(8, shape.CornerRadiusX);
        vm.UndoCommand.Execute(null); Assert.Equal(new SKRect(0, 0, 100, 60), shape.Rect);
        Assert.False(vm.HasUnsavedChanges);
        vm.RedoCommand.Execute(null); Assert.Equal(180, shape.Rect.Width, 3);
    }

    [Fact]
    public void Resize_CancelAndToolSwitchRestoreGeometry()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 100, 60) };
        var vm = Workspace(shape, DrawingTool.Rectangle); vm.SelectNodes([shape]);
        vm.BeginInteraction(new(100, 60), false); vm.UpdateInteraction(new(180, 90), false, false);
        vm.CancelInteraction(); Assert.Equal(new SKRect(0, 0, 100, 60), shape.Rect);
        vm.BeginInteraction(new(100, 60), false); vm.UpdateInteraction(new(180, 90), false, false);
        vm.ChooseTool(DrawingTool.Select); Assert.Equal(new SKRect(0, 0, 100, 60), shape.Rect);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void Resize_ShiftLocksRatioAndAltKeepsCenter()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 100, 60) };
        var vm = Workspace(shape, DrawingTool.Rectangle); vm.SelectNodes([shape]);
        vm.BeginInteraction(new(100, 60), false); vm.UpdateInteraction(new(150, 80), true, true); vm.EndInteraction();
        Assert.Equal(50, shape.Rect.MidX, 3); Assert.Equal(30, shape.Rect.MidY, 3);
        Assert.Equal(100f / 60, shape.Rect.Width / shape.Rect.Height, 3);
    }

    [Fact]
    public void EllipseTool_ResizesArcGeometryWithoutChangingAngles()
    {
        var arc = new ArcShapeNode { Center = new(50, 30), RadiusX = 50, RadiusY = 30,
            StartAngleDegrees = 20, SweepAngleDegrees = 120 };
        var vm = Workspace(arc, DrawingTool.Ellipse); vm.SelectNodes([arc]);
        vm.BeginInteraction(new(100, 60), false); vm.UpdateInteraction(new(200, 120), false, false); vm.EndInteraction();
        Assert.Equal(100, arc.RadiusX); Assert.Equal(60, arc.RadiusY);
        Assert.Equal(20, arc.StartAngleDegrees); Assert.Equal(120, arc.SweepAngleDegrees);
    }

    [Fact]
    public void ForceNew_CanDrawOverMatchingShapeAndFreeformDoesNotMatchPolygonTool()
    {
        var shape = new RectangleShapeNode { Rect = new(0, 0, 100, 60) };
        var vm = Workspace(shape, DrawingTool.Rectangle);
        vm.BeginInteraction(new(20, 20), false, true); vm.UpdateInteraction(new(60, 50), false, false); vm.EndInteraction();
        Assert.Equal(2, vm.ActiveLayer!.Children.Count);
        Assert.False(ShapeResizeGesture.Matches(DrawingTool.Polygon, new PolygonShapeNode()));
    }
}
