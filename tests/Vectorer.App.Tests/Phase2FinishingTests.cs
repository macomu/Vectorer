using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Tools;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class Phase2FinishingTests
{
    [Fact]
    public void ObjectEdit_DoesNotResetUnchangedLayerRowsOrActiveLayer()
    {
        var vm = new MainWindowViewModel(); var layer = vm.ActiveLayer;
        var resets = 0; vm.Layers.CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };
        vm.ExecuteCommand(new Vectorer.Core.Commands.AddNodeCommand(layer!, new RectangleShapeNode()));
        Assert.Equal(0, resets); Assert.Same(layer, vm.ActiveLayer);
        vm.UndoCommand.Execute(null); Assert.Equal(0, resets); Assert.Same(layer, vm.ActiveLayer);
    }
    private sealed class FailingClipboard : Vectorer.App.Services.IEditorClipboard
    {
        public Task<string?> ReadAsync() => Task.FromResult<string?>(null);
        public Task WriteAsync(string text) => throw new InvalidOperationException("Unavailable");
    }
    [Fact]
    public async Task Cut_PreservesSelectionWhenClipboardWriteFails()
    {
        var vm = Workspace(); var shape = new RectangleShapeNode(); vm.ActiveLayer!.Children.Add(shape); vm.SelectNodes([shape]);
        vm.Clipboard = new FailingClipboard(); await vm.CutSelectionCommand.ExecuteAsync(null);
        Assert.Same(shape, Assert.Single(vm.ActiveLayer.Children)); Assert.Same(shape, Assert.Single(vm.SelectedNodes)); Assert.False(vm.IsBusy);
    }
    [Fact]
    public void Shortcuts_ZoomDoesNotBooleanAndUndoWorksAfterAnAppliedEdit()
    {
        var vm = Workspace(); var a = new RectangleShapeNode(); var b = new RectangleShapeNode();
        vm.ActiveLayer!.Children.AddRange([a, b]); vm.SelectNodes([a, b]);
        vm.TryHandleEditorShortcut(Avalonia.Input.Key.OemPlus, Avalonia.Input.KeyModifiers.Control);
        Assert.Equal(2, vm.ActiveLayer.Children.Count); Assert.Equal(2, vm.SelectedNodes.Count); Assert.True(vm.Zoom > 1);
        vm.TryHandleEditorShortcut(Avalonia.Input.Key.D, Avalonia.Input.KeyModifiers.Control); Assert.Equal(4, vm.ActiveLayer.Children.Count);
        vm.TryHandleEditorShortcut(Avalonia.Input.Key.Z, Avalonia.Input.KeyModifiers.Control); Assert.Equal(2, vm.ActiveLayer.Children.Count);
        Assert.False(vm.TryHandleEditorShortcut(Avalonia.Input.Key.E, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift));
        Assert.False(vm.TryHandleEditorShortcut(Avalonia.Input.Key.D, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Alt));
        vm.SelectNodes([a, b]); vm.BooleanDifferenceActiveLayerShapesCommand.Execute(null); Assert.Empty(vm.ActiveLayer.Children);
        vm.UndoCommand.Execute(null); Assert.Equal(new SceneNode[] { a, b }, vm.ActiveLayer.Children);
    }
    private static MainWindowViewModel Workspace()
    {
        var vm = new MainWindowViewModel(); vm.CurrentDocument.Layers.Clear(); var layer = new LayerNode();
        vm.CurrentDocument.Layers.Add(layer); vm.ActiveLayer = layer; vm.SelectNodes([]); return vm;
    }
    [Fact]
    public void NodeCoordinateInput_UpdatesOnlySpecifiedCoordinateAndPreservesHandleOffsets()
    {
        var vm = Workspace(); var path = new PathShapeNode("M0 10 C10 20 20 30 30 40");
        vm.ActiveLayer!.Children.Add(path); vm.SelectNodes([path]); vm.ChooseTool(DrawingTool.Node);
        var tool = Assert.IsType<NodeEditTool>(vm.CanvasTool); tool.Synchronize();
        foreach (var a in tool.Model.Contours[0].Anchors) tool.SelectedAnchors.Add(a);
        vm.RequestRedraw(); Assert.Null(vm.NodeY);
        vm.NodeX = 50; vm.ApplyNodeCoordinatesCommand.Execute(null);
        Assert.All(tool.Model.Contours[0].Anchors, a => Assert.Equal(50, a.Position.X));
        Assert.Equal(10, tool.Model.Contours[0].Anchors[0].Position.Y); Assert.Equal(40, tool.Model.Contours[0].Anchors[1].Position.Y);
        Assert.Equal(new SKPoint(60, 20), tool.Model.Contours[0].Anchors[0].Outgoing);
        vm.UndoCommand.Execute(null); Assert.Equal("M0 10 C10 20 20 30 30 40", path.SvgPathData);
        Assert.False(vm.HasSelectedAnchors); Assert.Null(vm.NodeX); Assert.Null(vm.NodeY);
    }
    [Fact]
    public void JoinPaths_IsOneUndoStepAndRestoresOriginalOrder()
    {
        var vm = Workspace(); var a = new PathShapeNode("M0 0 L10 0"); var between = new RectangleShapeNode(); var b = new PathShapeNode("M20 0 L30 0");
        vm.ActiveLayer!.Children.AddRange([a, between, b]); vm.SelectNodes([b, a]); vm.JoinPathCommand.Execute(null);
        Assert.Equal(2, vm.ActiveLayer.Children.Count); Assert.IsType<PathShapeNode>(vm.ActiveLayer.Children[0]); Assert.Same(between, vm.ActiveLayer.Children[1]);
        vm.UndoCommand.Execute(null); Assert.Equal(new SceneNode[] { a, between, b }, vm.ActiveLayer.Children);
    }
    [Fact]
    public async Task CopyPaste_ClonesIdsAndPreservesWorldPositionInsideTransformedGroup()
    {
        var vm = Workspace(); var source = new RectangleShapeNode { Rect = new(10, 20, 50, 80), Transform = SKMatrix.CreateTranslation(15, 10) };
        var group = new GroupNode { Transform = SKMatrix.CreateScale(2, 3) };
        vm.ActiveLayer!.Children.AddRange([source, group]); vm.SelectNodes([source]); await vm.CopySelectionCommand.ExecuteAsync(null);
        vm.SelectNodes([group]); vm.EnterGroupCommand.Execute(null); await vm.PasteCommand.ExecuteAsync(null);
        var pasted = Assert.Single(group.Children); Assert.NotEqual(source.Id, pasted.Id);
        using var before = source.BuildTransformedPath(); using var after = CanvasGeometry.BuildWorldPath(group, SKMatrix.CreateIdentity());
        Assert.Equal(before.Bounds, after.Bounds);
        vm.UndoCommand.Execute(null); Assert.Empty(group.Children);
    }
    [Fact]
    public void DuplicateOrderAndAlign_KeepSiblingOrderAndRespectWorldTransforms()
    {
        var vm = Workspace(); vm.ActiveLayer!.Transform = SKMatrix.CreateScale(2, 2);
        var a = new RectangleShapeNode { Rect = new(0, 0, 10, 10) }; var b = new RectangleShapeNode { Rect = new(40, 20, 60, 40) }; var c = new RectangleShapeNode();
        vm.ActiveLayer.Children.AddRange([a, b, c]); vm.SelectNodes([a, b]); vm.DuplicateSelectionCommand.Execute(null);
        Assert.Equal(5, vm.ActiveLayer.Children.Count); Assert.Same(a, vm.ActiveLayer.Children[0]); Assert.Same(b, vm.ActiveLayer.Children[2]);
        vm.UndoCommand.Execute(null); vm.SelectNodes([a, b]); vm.ChangeOrderCommand.Execute("front");
        Assert.Equal(new SceneNode[] { c, a, b }, vm.ActiveLayer.Children); vm.UndoCommand.Execute(null);
        vm.SelectNodes([a, b]); vm.AlignSelectionCommand.Execute("left");
        using var aPath = CanvasGeometry.BuildWorldPath(a, vm.ActiveLayer.Transform); using var bPath = CanvasGeometry.BuildWorldPath(b, vm.ActiveLayer.Transform);
        Assert.Equal(aPath.Bounds.Left, bPath.Bounds.Left);
        vm.UndoCommand.Execute(null); Assert.True(b.Transform.IsIdentity);
    }
    [Fact]
    public void RectangleCorners_CanBeIndependentLinkedAndReset()
    {
        var vm = Workspace(); var rectangle = new RectangleShapeNode(); vm.ActiveLayer!.Children.Add(rectangle); vm.SelectNodes([rectangle]);
        vm.LinkCorners = false; vm.CornerRadius = 12; vm.CornerRadiusY = 24;
        Assert.Equal(12, rectangle.CornerRadiusX); Assert.Equal(24, rectangle.CornerRadiusY);
        vm.ResetCornersCommand.Execute(null); Assert.Equal(0, rectangle.CornerRadiusX); Assert.Equal(0, rectangle.CornerRadiusY);
        vm.UndoCommand.Execute(null); Assert.Equal(12, rectangle.CornerRadiusX); Assert.Equal(24, rectangle.CornerRadiusY);
        vm.SelectNodes([rectangle]); vm.LinkCorners = true; vm.CornerRadius = 6; Assert.Equal(6, rectangle.CornerRadiusY);
    }
    [Fact]
    public void ShapeDetails_ChangesEllipseModeAndKeepsOriginalIdAndTransform()
    {
        var vm = Workspace(); var ellipse = new EllipseShapeNode { Transform = SKMatrix.CreateTranslation(30, 40) };
        vm.ActiveLayer!.Children.Add(ellipse); vm.SelectNodes([ellipse]);
        vm.EllipseMode = "弦"; vm.GeometryX = 100; vm.GeometryY = 120; vm.RadiusX = 60; vm.RadiusY = 30;
        vm.ApplyShapeDetailsCommand.Execute(null);
        var arc = Assert.IsType<ArcShapeNode>(vm.ActiveLayer.Children[0]); Assert.Equal(ArcCloseMode.Chord, arc.CloseMode);
        Assert.Equal(ellipse.Id, arc.Id); Assert.Equal(ellipse.Transform, arc.Transform); Assert.Equal(new SKPoint(100, 120), arc.Center);
        vm.UndoCommand.Execute(null); Assert.Same(ellipse, vm.ActiveLayer.Children[0]);
    }
    [Fact]
    public void MergeLayers_PreservesWorldGeometryAndUndoesAttributesAndOrder()
    {
        var vm = Workspace(); var lower = vm.ActiveLayer!; lower.Transform = SKMatrix.CreateScale(2, 2); lower.Opacity = 0.5f;
        var upper = new LayerNode { Transform = SKMatrix.CreateTranslation(80, 40) }; var shape = new RectangleShapeNode { Rect = new(0, 0, 10, 10) }; upper.Children.Add(shape);
        vm.CurrentDocument.Layers.Add(upper); vm.ActiveLayer = upper;
        using var original = CanvasGeometry.BuildWorldPath(shape, upper.Transform);
        vm.MergeLayerDownCommand.Execute(null); Assert.Single(vm.CurrentDocument.Layers); Assert.Equal(0.5f, lower.Opacity);
        using var merged = CanvasGeometry.BuildWorldPath(shape, lower.Transform); Assert.Equal(original.Bounds, merged.Bounds);
        vm.UndoCommand.Execute(null); Assert.Equal(new[] { lower, upper }, vm.CurrentDocument.Layers); Assert.Same(shape, Assert.Single(upper.Children)); Assert.True(shape.Transform.IsIdentity);
    }
}
