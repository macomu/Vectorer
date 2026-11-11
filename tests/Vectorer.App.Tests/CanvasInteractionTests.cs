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
}
