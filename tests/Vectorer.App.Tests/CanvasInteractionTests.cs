using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Tools;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class CanvasInteractionTests
{
    [Fact]
    public void SynchronizeNodeOverlay_DoesNotInvalidateTheActiveRenderPass()
    {
        var vm = CreateWorkspace(); var path = new PathShapeNode("M10 20 C20 0 80 0 100 20");
        vm.ActiveLayer!.Children.Add(path); vm.SelectNodes([path]); vm.ChooseTool(DrawingTool.Node);
        var version = vm.RenderVersion;
        var tool = Assert.IsType<NodeEditTool>(vm.CanvasTool); tool.Synchronize();
        Assert.Equal(version, vm.RenderVersion); Assert.Equal(2, tool.Model.Contours[0].Anchors.Count);
    }
    private static MainWindowViewModel CreateWorkspace()
    {
        var vm = new MainWindowViewModel();
        vm.CurrentDocument.Layers.Clear();
        vm.CurrentDocument.Layers.Add(new LayerNode());
        vm.ActiveLayer = vm.CurrentDocument.Layers[0];
        vm.SelectNodes([]);
        return vm;
    }
    [Fact]
    public void ShapeTool_PreviewsThenCommitsOneUndoableOperation()
    {
        var vm = CreateWorkspace(); vm.ChooseTool(DrawingTool.Rectangle);
        vm.BeginInteraction(new(10, 20), false); vm.UpdateInteraction(new(100, 80), false, false);
        Assert.Empty(vm.ActiveLayer!.Children); Assert.NotNull(vm.PreviewShape);
        vm.EndInteraction(); Assert.Single(vm.ActiveLayer.Children);
        vm.UndoCommand.Execute(null); Assert.Empty(vm.ActiveLayer.Children);
        vm.RedoCommand.Execute(null); Assert.Single(vm.ActiveLayer.Children);
    }
    [Fact]
    public void SwitchingTools_CancelsUncommittedShape()
    {
        var vm = CreateWorkspace(); vm.ChooseTool(DrawingTool.Rectangle);
        vm.BeginInteraction(new(10, 20), false); vm.UpdateInteraction(new(100, 80), false, false);
        vm.ChooseTool(DrawingTool.Ellipse);
        Assert.Empty(vm.ActiveLayer!.Children); Assert.Null(vm.PreviewShape); Assert.False(vm.IsInteracting);
    }
    [Fact]
    public void SelectionTool_CancelRestoresTransformAndClickDoesNotSnapMove()
    {
        var vm = CreateWorkspace(); var rect = new RectangleShapeNode { Rect = new(10, 10, 100, 100) };
        vm.ActiveLayer!.Children.Add(rect); vm.SnapToGrid = true;
        vm.BeginInteraction(new(55, 55), false); vm.UpdateInteraction(new(55, 55), false, false); vm.EndInteraction();
        Assert.True(rect.Transform.IsIdentity);
        vm.BeginInteraction(new(55, 55), false); vm.UpdateInteraction(new(155, 155), false, false); vm.CancelInteraction();
        Assert.True(rect.Transform.IsIdentity);
    }
    [Fact]
    public void MixedStyleWidthEdit_PreservesIndividualColorsAndUndoes()
    {
        var vm = CreateWorkspace(); var a = new RectangleShapeNode(); var b = new RectangleShapeNode();
        a.Style.Fill = new SolidColorFill(SKColors.Red); b.Style.Fill = new SolidColorFill(SKColors.Blue);
        a.Style.Stroke = new StrokeStyle(SKColors.Green, 2); b.Style.Stroke = new StrokeStyle(SKColors.Yellow, 8);
        vm.ActiveLayer!.Children.AddRange([a, b]); vm.SelectNodes([a, b]);
        Assert.Equal("", vm.FillColor); Assert.Null(vm.StrokeWidth);
        vm.StrokeWidth = 5; Assert.Equal(5, a.Style.Stroke.Width); Assert.Equal(5, b.Style.Stroke.Width);
        Assert.Equal(new SolidColorFill(SKColors.Red), a.Style.Fill); Assert.Equal(SKColors.Yellow, b.Style.Stroke.Color);
        vm.UndoCommand.Execute(null); Assert.Equal(2, a.Style.Stroke.Width); Assert.Equal(8, b.Style.Stroke.Width);
    }
    [Fact]
    public void BezierPen_RetainsUncommittedPointsAcrossClicksAndCommitsOnFinish()
    {
        var vm = CreateWorkspace(); vm.ChooseTool(DrawingTool.Bezier);
        vm.BeginInteraction(new(10, 20), false); vm.EndInteraction();
        vm.BeginInteraction(new(100, 80), false); vm.UpdateInteraction(new(130, 100), false, false); vm.EndInteraction();
        Assert.True(vm.IsInteracting); Assert.Empty(vm.ActiveLayer!.Children);
        Assert.IsType<BezierPenTool>(vm.CanvasTool).Finish();
        var path = Assert.IsType<PathShapeNode>(Assert.Single(vm.ActiveLayer.Children));
        Assert.Contains("C", path.SvgPathData); vm.UndoCommand.Execute(null); Assert.Empty(vm.ActiveLayer.Children);
    }
    [Fact]
    public void PolygonNodeEdit_BecomesFreeformAndUndoRestoresParameters()
    {
        var vm = CreateWorkspace(); var polygon = PolygonShapeNode.CreateRegularConvex(new(100, 100), 50, 4, 0);
        vm.ActiveLayer!.Children.Add(polygon); vm.SelectNodes([polygon]); vm.ChooseTool(DrawingTool.Node);
        vm.BeginInteraction(new(150, 100), false); vm.UpdateInteraction(new(170, 110), false, false); vm.EndInteraction();
        Assert.Equal(PolygonKind.Freeform, polygon.PolygonKind); Assert.Equal(new SKPoint(170, 110), polygon.Points[0]);
        vm.UndoCommand.Execute(null); Assert.Equal(PolygonKind.RegularConvex, polygon.PolygonKind); Assert.Equal(new SKPoint(150, 100), polygon.Points[0]);
    }
    [Fact]
    public void PolygonNodeClick_DoesNotConvertToFreeformOrCreateUndoEntry()
    {
        var vm = CreateWorkspace(); var polygon = PolygonShapeNode.CreateRegularConvex(new(100, 100), 50, 4, 0);
        vm.ActiveLayer!.Children.Add(polygon); vm.SelectNodes([polygon]); vm.ChooseTool(DrawingTool.Node);
        vm.BeginInteraction(new(150, 100), false); vm.UpdateInteraction(new(150, 100), false, false); vm.EndInteraction();
        Assert.Equal(PolygonKind.RegularConvex, polygon.PolygonKind); Assert.False(vm.HasUnsavedChanges);
    }
    [Fact]
    public void NodeDeletion_AllNodesDeletesShapeInOneUndoStep()
    {
        var vm = CreateWorkspace(); var path = new PathShapeNode("M10 10 L100 10 L100 100");
        vm.ActiveLayer!.Children.Add(path); vm.SelectNodes([path]); vm.ChooseTool(DrawingTool.Node);
        var tool = Assert.IsType<NodeEditTool>(vm.CanvasTool); tool.Synchronize();
        foreach (var anchor in tool.Model.Contours[0].Anchors) tool.SelectedAnchors.Add(anchor);
        tool.DeleteNodes(); Assert.Empty(vm.ActiveLayer.Children);
        vm.UndoCommand.Execute(null); Assert.Same(path, Assert.Single(vm.ActiveLayer.Children));
        Assert.Equal("M10 10 L100 10 L100 100", path.SvgPathData);
    }
    [Fact]
    public void GroupIsolation_CreationUsesAccumulatedTransformAndEscapeExitsScope()
    {
        var vm = CreateWorkspace(); var group = new GroupNode { Transform = SKMatrix.CreateTranslation(80, 40) };
        vm.ActiveLayer!.Transform = SKMatrix.CreateScale(2, 2); vm.ActiveLayer.Children.Add(group);
        vm.SelectNodes([group]); vm.EnterGroupCommand.Execute(null); Assert.True(vm.IsInGroup);
        vm.ChooseTool(DrawingTool.Rectangle); vm.BeginInteraction(new(200, 120), false); vm.UpdateInteraction(new(280, 180), false, false); vm.EndInteraction();
        var shape = Assert.IsType<RectangleShapeNode>(Assert.Single(group.Children));
        using var world = CanvasGeometry.BuildWorldPath(group, vm.ActiveLayer.Transform);
        Assert.Equal(new SKRect(200, 120, 280, 180), world.Bounds);
        vm.HandleEscape(); Assert.Empty(vm.SelectedNodes); Assert.True(vm.IsInGroup);
        vm.HandleEscape(); Assert.False(vm.IsInGroup);
    }
    [Fact]
    public void CornerHandle_ChangesRadiusAndSupportsUndo()
    {
        var vm = CreateWorkspace(); var rectangle = new RectangleShapeNode { Rect = new(10, 10, 110, 110) };
        vm.ActiveLayer!.Children.Add(rectangle); vm.SelectNodes([rectangle]);
        var handle = ShapeParameterGesture.GetHandles(vm)[0];
        vm.BeginInteraction(handle.Position, false); vm.UpdateInteraction(new(80, 20), false, false); vm.EndInteraction();
        Assert.Equal(30, rectangle.CornerRadiusX);
        vm.UndoCommand.Execute(null); Assert.Equal(0, rectangle.CornerRadiusX);
    }
    [Fact]
    public void TextCommit_IsUndoableAndEscapedDraftIsDiscarded()
    {
        var vm = CreateWorkspace(); vm.BeginTextEdit(new(20, 80)); vm.CommitText(null);
        Assert.Empty(vm.ActiveLayer!.Children);
        vm.BeginTextEdit(new(20, 80)); vm.CommitText("Hello");
        var text = Assert.IsType<TextShapeNode>(Assert.Single(vm.ActiveLayer.Children)); Assert.Equal("Hello", text.Text);
        vm.UndoCommand.Execute(null); Assert.Empty(vm.ActiveLayer.Children);
    }
    [Fact]
    public void NumericTransform_PreservesLayerCoordinatesAndIsUndoable()
    {
        var vm = CreateWorkspace(); var rectangle = new RectangleShapeNode { Rect = new(0, 0, 20, 20) };
        vm.ActiveLayer!.Transform = SKMatrix.CreateScale(2, 2); vm.ActiveLayer.Children.Add(rectangle); vm.SelectNodes([rectangle]);
        vm.SelectionX = 100; vm.SelectionY = 80; vm.SelectionWidth = 60; vm.SelectionHeight = 50; vm.ApplyTransformCommand.Execute(null);
        Assert.Equal(new SKRect(100, 80, 160, 130), vm.GetSelectionBounds());
        vm.UndoCommand.Execute(null); Assert.True(rectangle.Transform.IsIdentity);
    }
    [Fact]
    public void SelectionTool_SecondClickTogglesRotationMode_AndCornerHandleRotatesWithUndo()
    {
        var vm = CreateWorkspace();
        var rect = new RectangleShapeNode { Rect = new(100, 100, 200, 160) };
        vm.ActiveLayer!.Children.Add(rect);
        var tool = Assert.IsType<SelectionTool>(vm.CanvasTool);

        // First click selects in Scale mode
        vm.BeginInteraction(new(150, 130), false);
        vm.EndInteraction();
        Assert.Same(rect, Assert.Single(vm.SelectedNodes));
        Assert.Equal(SelectionTransformMode.Scale, tool.TransformMode);
        Assert.False(vm.HasUnsavedChanges);

        // Second click on selected object switches to Rotate mode
        vm.BeginInteraction(new(150, 130), false);
        vm.EndInteraction();
        Assert.Equal(SelectionTransformMode.Rotate, tool.TransformMode);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Contains("15°", vm.ToolHint);

        // Dragging top-right rotation handle 90° clockwise around center (150, 130)
        var bounds = vm.GetSelectionBounds()!.Value;
        var topRight = SelectionTool.GetRotationHandles(bounds, vm.Zoom)[1];
        var center = tool.GetPivot(bounds);
        var target = new SKPoint(center.X - (topRight.Y - center.Y), center.Y + (topRight.X - center.X));
        vm.BeginInteraction(topRight, false);
        vm.UpdateInteraction(target, true, false);
        vm.EndInteraction();

        Assert.Equal(SelectionTransformMode.Rotate, tool.TransformMode);
        Assert.True(vm.HasUnsavedChanges);
        var rotatedTopLeft = rect.Transform.MapPoint(new SKPoint(100, 100));
        Assert.Equal(180f, rotatedTopLeft.X, 2);
        Assert.Equal(80f, rotatedTopLeft.Y, 2);

        // Dragging body (away from pivot) in Rotate mode moves without toggling back to Scale mode
        vm.BeginInteraction(new(135, 115), false);
        vm.UpdateInteraction(new(155, 125), false, false);
        vm.EndInteraction();
        Assert.Equal(SelectionTransformMode.Rotate, tool.TransformMode);
        var movedCenter = rect.Transform.MapPoint(new SKPoint(150, 130));
        Assert.Equal(170f, movedCenter.X, 2);
        Assert.Equal(140f, movedCenter.Y, 2);

        // Third click (no drag) at center toggles back to Scale mode
        vm.BeginInteraction(new(170, 140), false);
        vm.EndInteraction();
        Assert.Equal(SelectionTransformMode.Scale, tool.TransformMode);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.True(rect.Transform.IsIdentity);
    }
    [Fact]
    public void SelectionTool_CustomPivotRotatesAroundMovedPivot_AndShiftClickResetsPivot()
    {
        var vm = CreateWorkspace();
        var rect = new RectangleShapeNode { Rect = new(100, 100, 200, 200) };
        vm.ActiveLayer!.Children.Add(rect);
        var tool = Assert.IsType<SelectionTool>(vm.CanvasTool);

        // Select and click again to enter Rotate mode
        vm.BeginInteraction(new(150, 150), false); vm.EndInteraction();
        vm.BeginInteraction(new(150, 150), false); vm.EndInteraction();
        Assert.Equal(SelectionTransformMode.Rotate, tool.TransformMode);

        // Drag pivot from (150, 150) to top-left corner (100, 100)
        vm.BeginInteraction(new(150, 150), false);
        vm.UpdateInteraction(new(100, 100), false, false);
        vm.EndInteraction();
        Assert.Equal(new SKPoint(100, 100), tool.CustomPivot);
        Assert.False(vm.HasUnsavedChanges);

        // Rotate 90° clockwise around custom pivot (100, 100) using bottom-right handle
        var bounds = vm.GetSelectionBounds()!.Value;
        var bottomRight = SelectionTool.GetRotationHandles(bounds, vm.Zoom)[2];
        var target = new SKPoint(100f - (bottomRight.Y - 100f), 100f + (bottomRight.X - 100f));
        vm.BeginInteraction(bottomRight, false);
        vm.UpdateInteraction(target, true, false);
        vm.EndInteraction();

        // Top-left (100, 100) stays fixed; bottom-right (200, 200) maps to (0, 200)
        var mappedTopLeft = rect.Transform.MapPoint(new SKPoint(100, 100));
        Assert.Equal(100f, mappedTopLeft.X, 2);
        Assert.Equal(100f, mappedTopLeft.Y, 2);
        var mappedBottomRight = rect.Transform.MapPoint(new SKPoint(200, 200));
        Assert.Equal(0f, mappedBottomRight.X, 2);
        Assert.Equal(200f, mappedBottomRight.Y, 2);

        // Shift+click on custom pivot resets it to center
        vm.BeginInteraction(new(100, 100), true);
        vm.EndInteraction();
        Assert.Null(tool.CustomPivot);
    }
    [Fact]
    public void SelectionTool_SkewHandlesAndSelectionChangeReset()
    {
        var vm = CreateWorkspace();
        var a = new RectangleShapeNode { Rect = new(100, 100, 200, 200) };
        var b = new RectangleShapeNode { Rect = new(300, 100, 360, 160) };
        vm.ActiveLayer!.Children.AddRange([a, b]);
        var tool = Assert.IsType<SelectionTool>(vm.CanvasTool);

        vm.BeginInteraction(new(150, 150), false); vm.EndInteraction();
        vm.BeginInteraction(new(150, 150), false); vm.EndInteraction();
        Assert.Equal(SelectionTransformMode.Rotate, tool.TransformMode);

        // Drag top skew handle right; bottom edge remains fixed
        var bounds = vm.GetSelectionBounds()!.Value;
        var topSkew = SelectionTool.GetSkewHandles(bounds, vm.Zoom)[0];
        vm.BeginInteraction(topSkew, false);
        vm.UpdateInteraction(new(topSkew.X + 40, topSkew.Y), false, false);
        vm.EndInteraction();

        var mappedTopLeft = a.Transform.MapPoint(new SKPoint(100, 100));
        var mappedBottomLeft = a.Transform.MapPoint(new SKPoint(100, 200));
        Assert.True(mappedTopLeft.X > 130f);
        Assert.Equal(100f, mappedTopLeft.Y, 2);
        Assert.Equal(100f, mappedBottomLeft.X, 2);
        Assert.Equal(200f, mappedBottomLeft.Y, 2);

        // Selecting a different object resets back to Scale mode
        vm.BeginInteraction(new(330, 130), false);
        vm.EndInteraction();
        Assert.Same(b, Assert.Single(vm.SelectedNodes));
        Assert.Equal(SelectionTransformMode.Scale, tool.TransformMode);
    }
}
