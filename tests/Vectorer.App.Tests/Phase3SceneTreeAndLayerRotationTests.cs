using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class Phase3SceneTreeAndLayerRotationTests
{
    private static MainWindowViewModel CreateWorkspace()
    {
        var vm = new MainWindowViewModel();
        vm.CurrentDocument.Layers.Clear();
        var layer = new LayerNode { Name = "Layer 1" };
        vm.CurrentDocument.Layers.Add(layer);
        vm.ActiveLayer = layer;
        vm.SelectNodes([]);
        return vm;
    }

    [Fact]
    public void SceneTree_SyncsHierarchyBadgesSelectionVisibilityLockAndInlineRename()
    {
        var vm = CreateWorkspace();
        var layer = vm.ActiveLayer!;
        layer.BlendMode = LayerBlendMode.Overlay;

        var group = new GroupNode { Name = "Group A" };
        var star = PolygonShapeNode.CreateConcaveStar(new SKPoint(100, 100), 50, 22, 5);
        star.Name = "Star 1";
        star.Filter = new FilterSettings(4f, 4f);
        group.Children.Add(star);
        layer.Children.Add(group);
        vm.SyncSceneTree();

        var layerItem = Assert.Single(vm.SceneTree);
        Assert.Equal("[Overlay]", layerItem.BlendBadge);
        var groupItem = Assert.Single(layerItem.Children);
        var starItem = Assert.Single(groupItem.Children);
        Assert.Equal("[fx]", starItem.EffectBadge);

        // Selecting starItem in tree selects Star 1 on canvas and enters its parent group scope
        vm.SelectedTreeItem = starItem;
        Assert.Same(star, Assert.Single(vm.SelectedNodes));
        Assert.Same(group, vm.EditingGroup);

        // Inline rename commit & undo
        vm.BeginRenameNodeCommand.Execute(starItem);
        Assert.True(starItem.IsEditingName);
        starItem.EditingNameText = "Glowing Star";
        vm.CommitRenameNodeCommand.Execute(starItem);
        Assert.False(starItem.IsEditingName);
        Assert.Equal("Glowing Star", star.Name);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Star 1", star.Name);

        // Toggle lock deselects locked node and supports undo
        vm.SelectNodes([star]);
        vm.ToggleNodeLockCommand.Execute(starItem);
        Assert.True(star.IsLocked);
        Assert.Empty(vm.SelectedNodes);
        vm.UndoCommand.Execute(null);
        Assert.False(star.IsLocked);
    }

    [Fact]
    public void TryMoveTreeNode_PreservesWorldCoordinatesAcrossTransformedContainersAndGuardsInvalidTargets()
    {
        var vm = CreateWorkspace();
        var layer1 = vm.ActiveLayer!;
        layer1.Transform = SKMatrix.CreateTranslation(40f, 20f);

        var rect = new RectangleShapeNode
        {
            Name = "Moving Rect",
            Rect = new SKRect(10, 10, 50, 30),
            Transform = SKMatrix.CreateTranslation(15f, 5f)
        };
        layer1.Children.Add(rect);

        var layer2 = new LayerNode
        {
            Name = "Layer 2",
            Transform = SKMatrix.CreateScale(2f, 2f)
        };
        var targetGroup = new GroupNode
        {
            Name = "Target Group",
            Transform = SKMatrix.CreateTranslation(10f, 10f)
        };
        layer2.Children.Add(targetGroup);
        vm.CurrentDocument.Layers.Add(layer2);
        vm.SyncSceneTree();

        using var beforeWorldPath = CanvasGeometry.BuildWorldPath(rect, layer1.Transform);
        var expectedWorldBounds = beforeWorldPath.Bounds;

        // Move rect into targetGroup inside layer2
        Assert.True(vm.TryMoveTreeNode(rect, targetGroup, TreeDropPosition.Inside));
        Assert.Empty(layer1.Children);
        Assert.Same(rect, Assert.Single(targetGroup.Children));

        var newParentWorld = SKMatrix.Concat(layer2.Transform, targetGroup.Transform);
        using var afterWorldPath = CanvasGeometry.BuildWorldPath(rect, newParentWorld);
        Assert.Equal(expectedWorldBounds.Left, afterWorldPath.Bounds.Left, 3);
        Assert.Equal(expectedWorldBounds.Top, afterWorldPath.Bounds.Top, 3);
        Assert.Equal(expectedWorldBounds.Width, afterWorldPath.Bounds.Width, 3);
        Assert.Equal(expectedWorldBounds.Height, afterWorldPath.Bounds.Height, 3);

        // Undo restores original parent, index, and local transform
        vm.UndoCommand.Execute(null);
        Assert.Same(rect, Assert.Single(layer1.Children));
        Assert.Empty(targetGroup.Children);
        Assert.Equal(SKMatrix.CreateTranslation(15f, 5f), rect.Transform);

        // Moving into a singular (non-invertible) container is rejected
        targetGroup.Transform = SKMatrix.CreateScale(0f, 1f);
        Assert.False(vm.TryMoveTreeNode(rect, targetGroup, TreeDropPosition.Inside));
        Assert.Same(rect, Assert.Single(layer1.Children));

        // Layer reordering via TryMoveTreeNode
        Assert.True(vm.TryMoveTreeNode(layer2, layer1, TreeDropPosition.Before));
        Assert.Equal(new[] { layer2, layer1 }, vm.CurrentDocument.Layers);
        vm.UndoCommand.Execute(null);
        Assert.Equal(new[] { layer1, layer2 }, vm.CurrentDocument.Layers);
    }

    [Fact]
    public void RotateLayerViewModel_SupportsCanvasAndElementBoundsCentersWithLivePreviewCancelAndCommit()
    {
        var vm = CreateWorkspace();
        vm.CurrentDocument.Width = 800f;
        vm.CurrentDocument.Height = 600f;
        vm.CurrentDocument.ViewBox = new SKRect(0, 0, 800, 600);
        var layer = vm.ActiveLayer!;
        var rect = new RectangleShapeNode { Rect = new SKRect(100, 100, 200, 200) };
        layer.Children.Add(rect);

        var rotateVm = new RotateLayerViewModel(vm, layer, 0m);
        Assert.Equal(new SKPoint(400f, 300f), rotateVm.GetRotationCenter());

        rotateVm.CenterMode = LayerRotationCenterMode.LayerBoundsCenter;
        Assert.Equal(new SKPoint(150f, 150f), rotateVm.GetRotationCenter());

        // Live preview updates layer transform without recording an Undo command
        rotateVm.Degrees = 90m;
        Assert.False(layer.Transform.IsIdentity);
        Assert.False(vm.HasUnsavedChanges);

        // CancelPreview restores identity transform
        rotateVm.CancelPreview();
        Assert.True(layer.Transform.IsIdentity);

        // Commit records a single undoable command rotating around (150, 150)
        rotateVm.Degrees = 90m;
        rotateVm.Commit();
        Assert.True(vm.HasUnsavedChanges);
        using var rotatedPath = CanvasGeometry.BuildWorldPath(rect, layer.Transform);
        Assert.Equal(100f, rotatedPath.Bounds.Left, 2);
        Assert.Equal(100f, rotatedPath.Bounds.Top, 2);
        Assert.Equal(200f, rotatedPath.Bounds.Right, 2);
        Assert.Equal(200f, rotatedPath.Bounds.Bottom, 2);

        vm.UndoCommand.Execute(null);
        Assert.True(layer.Transform.IsIdentity);
    }
}
