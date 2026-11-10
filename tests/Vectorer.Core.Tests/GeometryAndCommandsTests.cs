using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Geometry;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;
using Vectorer.Plugins;
using Vectorer.SamplePlugin;
using Xunit;

namespace Vectorer.Core.Tests;

public sealed class GeometryAndCommandsTests
{
    [Fact]
    public void PolygonShapeNode_CreatesConvexAndConcavePolygons()
    {
        var convexHex = PolygonShapeNode.CreateRegularConvex(new SKPoint(100, 100), 50f, sides: 6);
        var concaveStar = PolygonShapeNode.CreateConcaveStar(new SKPoint(100, 100), 60f, 25f, pointsCount: 5);

        Assert.Equal(6, convexHex.Points.Count);
        Assert.Equal(10, concaveStar.Points.Count);

        using var hexPath = convexHex.BuildLocalPath();
        using var starPath = concaveStar.BuildLocalPath();
        Assert.False(hexPath.IsEmpty);
        Assert.False(starPath.IsEmpty);
    }

    [Theory]
    [InlineData(BooleanOperationType.Union)]
    [InlineData(BooleanOperationType.Difference)]
    [InlineData(BooleanOperationType.Intersection)]
    [InlineData(BooleanOperationType.Xor)]
    public void PathBooleanService_CombinesOverlappingShapes(BooleanOperationType operation)
    {
        var circle = new EllipseShapeNode
        {
            Center = new SKPoint(100, 100),
            RadiusX = 50,
            RadiusY = 50
        };
        var rect = new RectangleShapeNode
        {
            Rect = new SKRect(100, 100, 180, 180)
        };

        var service = new PathBooleanService();
        var combined = service.Combine(circle, rect, operation);

        Assert.NotNull(combined);
        Assert.False(string.IsNullOrWhiteSpace(combined.SvgPathData));

        using var path = combined.BuildLocalPath();
        Assert.False(path.IsEmpty);
    }

    [Fact]
    public void UndoRedoService_ExecutesUndoesAndRedoesNodeCommands()
    {
        var layer = new LayerNode { Name = "Layer 1" };
        var arc = new ArcShapeNode
        {
            Center = new SKPoint(120, 120),
            RadiusX = 40,
            RadiusY = 40
        };

        var undoRedo = new UndoRedoService();
        undoRedo.ExecuteCommand(new AddNodeCommand(layer, arc));

        Assert.Single(layer.Children);
        Assert.True(undoRedo.CanUndo);

        undoRedo.Undo();
        Assert.Empty(layer.Children);
        Assert.True(undoRedo.CanRedo);

        undoRedo.Redo();
        Assert.Single(layer.Children);
    }

    [Fact]
    public void PluginHostService_RegistersSamplePluginContributions()
    {
        var host = new PluginHostService();
        var loadedCount = host.LoadFromAssembly(typeof(SampleVectorerPlugin).Assembly);

        Assert.Equal(1, loadedCount);
        Assert.Single(host.LoadedPlugins);
        Assert.NotEmpty(host.MenuContributions);
        Assert.NotEmpty(host.ToolbarContributions);
        Assert.Contains(host.ShaderProviders, s => s.ShaderId == SampleVectorerPlugin.SunsetWaveShaderId);
    }
}
