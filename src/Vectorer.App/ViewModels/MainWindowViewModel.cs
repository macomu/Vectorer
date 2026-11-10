using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Geometry;
using Vectorer.Abstractions.Plugins;
using Vectorer.Abstractions.Services;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;
using Vectorer.Plugins;
using Vectorer.Rendering;
using Vectorer.Rendering.Export;
using Vectorer.Rendering.Shaders;
using Vectorer.SamplePlugin;
using Vectorer.Svg;

namespace Vectorer.App.ViewModels;

/// <summary>
/// Main ViewModel for the Vectorer editor window. Implements <see cref="IEditorWorkspace"/> so plugins
/// can inspect and modify the active SVG document.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IEditorWorkspace
{
    private readonly IUndoRedoService _undoRedoService;
    private readonly IPathBooleanService _booleanService;
    private readonly ISvgSerializer _svgSerializer;
    private readonly IPngExportService _pngExportService;
    private readonly PluginHostService _pluginHost;
    private readonly ShaderEffectRegistry _shaderRegistry;
    private readonly List<SceneNode> _selectedNodes = [];

    [ObservableProperty]
    private SvgDocument _currentDocument;

    [ObservableProperty]
    private LayerNode? _activeLayer;

    [ObservableProperty]
    private int _renderVersion;

    [ObservableProperty]
    private string _statusMessage = "Ready — Vectorer (.NET 10 + Avalonia + SkiaSharp)";

    public IDocumentRenderer DocumentRenderer { get; }

    public ObservableCollection<LayerNode> Layers { get; } = [];
    public ObservableCollection<IMenuContribution> PluginMenus { get; } = [];
    public ObservableCollection<IToolbarContribution> PluginToolbarItems { get; } = [];

    public IReadOnlyList<SceneNode> SelectedNodes => _selectedNodes;

    public MainWindowViewModel()
    {
        _undoRedoService = new UndoRedoService();
        _booleanService = new PathBooleanService();
        _svgSerializer = new SvgSerializer();
        _shaderRegistry = new ShaderEffectRegistry();
        DocumentRenderer = new SkiaDocumentRenderer(_shaderRegistry);
        _pngExportService = new PngExportService(DocumentRenderer);
        _pluginHost = new PluginHostService();

        _currentDocument = CreateInitialDemoDocument();
        SyncLayersCollection();

        LoadBuiltInAndExternalPlugins();
    }

    public void ExecuteCommand(IDocumentCommand command)
    {
        _undoRedoService.ExecuteCommand(command);
        SyncLayersCollection();
        RequestRedraw();
    }

    public void SelectNodes(IEnumerable<SceneNode> nodes)
    {
        _selectedNodes.Clear();
        _selectedNodes.AddRange(nodes);
        StatusMessage = $"Selected {_selectedNodes.Count} item(s).";
    }

    public void RequestRedraw()
    {
        RenderVersion++;
    }

    [RelayCommand]
    private void AddConvexPolygon()
    {
        var layer = EnsureActiveLayer();
        var hex = PolygonShapeNode.CreateRegularConvex(new SKPoint(220, 220), 85f, sides: 6);
        hex.Style.Fill = new SolidColorFill(new SKColor(56, 189, 248));
        hex.Style.Stroke = new StrokeStyle(new SKColor(15, 23, 42), 3f);

        ExecuteCommand(new AddNodeCommand(layer, hex, "Add Convex Hexagon"));
        SelectNodes([hex]);
        StatusMessage = "Added Convex Polygon (Hexagon).";
    }

    [RelayCommand]
    private void AddConcavePolygon()
    {
        var layer = EnsureActiveLayer();
        var star = PolygonShapeNode.CreateConcaveStar(new SKPoint(420, 240), outerRadius: 95f, innerRadius: 42f, pointsCount: 5);
        star.Style.Fill = new TextureFill(
            TexturePatternKind.Checkerboard,
            new SKColor(250, 204, 21),
            new SKColor(234, 88, 12),
            Scale: 18f);
        star.Style.Stroke = new StrokeStyle(new SKColor(30, 41, 59), 2.5f);

        ExecuteCommand(new AddNodeCommand(layer, star, "Add Concave Star"));
        SelectNodes([star]);
        StatusMessage = "Added Concave Polygon with Checkerboard Texture Fill.";
    }

    [RelayCommand]
    private void AddCircle()
    {
        var layer = EnsureActiveLayer();
        var circle = new EllipseShapeNode
        {
            Name = "Circle",
            Center = new SKPoint(320, 260),
            RadiusX = 80f,
            RadiusY = 80f
        };
        circle.Style.Fill = new SolidColorFill(new SKColor(168, 85, 247, 220));
        circle.Style.Stroke = new StrokeStyle(new SKColor(243, 232, 255), 3f);

        ExecuteCommand(new AddNodeCommand(layer, circle, "Add Circle"));
        SelectNodes([circle]);
        StatusMessage = "Added Circle.";
    }

    [RelayCommand]
    private void AddArc()
    {
        var layer = EnsureActiveLayer();
        var arc = new ArcShapeNode
        {
            Name = "Pie Arc",
            Center = new SKPoint(560, 260),
            RadiusX = 85f,
            RadiusY = 85f,
            StartAngleDegrees = -35f,
            SweepAngleDegrees = 260f,
            CloseMode = ArcCloseMode.PieSlice
        };
        arc.Style.Fill = new TextureFill(
            TexturePatternKind.DiagonalStripes,
            new SKColor(52, 211, 153),
            new SKColor(6, 95, 70),
            Scale: 16f);
        arc.Style.Stroke = new StrokeStyle(new SKColor(209, 250, 229), 2.5f);

        ExecuteCommand(new AddNodeCommand(layer, arc, "Add Arc"));
        SelectNodes([arc]);
        StatusMessage = "Added Pie Arc with Diagonal Stripe Texture.";
    }

    [RelayCommand]
    private void BooleanUnionActiveLayerShapes()
    {
        ApplyBooleanOnLastTwoShapes(BooleanOperationType.Union);
    }

    [RelayCommand]
    private void BooleanDifferenceActiveLayerShapes()
    {
        ApplyBooleanOnLastTwoShapes(BooleanOperationType.Difference);
    }

    [RelayCommand]
    private void ToggleGaussianBlurOnActiveLayer()
    {
        var layer = EnsureActiveLayer();
        layer.Filter = layer.Filter is { HasActiveFilter: true }
            ? null
            : new FilterSettings(BlurSigmaX: 6f, BlurSigmaY: 6f);
        RequestRedraw();
        StatusMessage = layer.Filter is not null
            ? $"Applied Gaussian Blur (σ=6) to '{layer.Name}'."
            : $"Cleared blur filter on '{layer.Name}'.";
    }

    [RelayCommand]
    private void CycleActiveLayerBlendMode()
    {
        var layer = EnsureActiveLayer();
        var modes = Enum.GetValues<LayerBlendMode>();
        var nextIndex = ((int)layer.BlendMode + 1) % modes.Length;
        layer.BlendMode = modes[nextIndex];
        RequestRedraw();
        StatusMessage = $"Layer '{layer.Name}' blend mode set to {layer.BlendMode}.";
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoRedoService.CanUndo)
        {
            _undoRedoService.Undo();
            SyncLayersCollection();
            RequestRedraw();
            StatusMessage = "Undo executed.";
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (_undoRedoService.CanRedo)
        {
            _undoRedoService.Redo();
            SyncLayersCollection();
            RequestRedraw();
            StatusMessage = "Redo executed.";
        }
    }

    [RelayCommand]
    private void ExecutePluginToolbarItem(IToolbarContribution? item)
    {
        if (item is null)
        {
            return;
        }

        item.Execute(this);
        StatusMessage = $"Executed plugin tool: {item.Title}";
    }

    private void ApplyBooleanOnLastTwoShapes(BooleanOperationType op)
    {
        var layer = EnsureActiveLayer();
        var shapes = layer.Children.OfType<ShapeNode>().TakeLast(2).ToList();
        if (shapes.Count < 2)
        {
            StatusMessage = "Need at least 2 shapes on the active layer for boolean operations.";
            return;
        }

        var combined = _booleanService.Combine(shapes[0], shapes[1], op);
        if (combined is null)
        {
            StatusMessage = $"Boolean {op} produced an empty shape.";
            return;
        }

        ExecuteCommand(new DelegateDocumentCommand(
            $"Boolean {op}",
            execute: () =>
            {
                layer.Children.Remove(shapes[0]);
                layer.Children.Remove(shapes[1]);
                layer.Children.Add(combined);
            },
            undo: () =>
            {
                layer.Children.Remove(combined);
                layer.Children.Add(shapes[0]);
                layer.Children.Add(shapes[1]);
            }));

        SelectNodes([combined]);
        StatusMessage = $"Applied Boolean {op} on '{shapes[0].Name}' and '{shapes[1].Name}'.";
    }

    private LayerNode EnsureActiveLayer()
    {
        if (ActiveLayer is not null)
        {
            return ActiveLayer;
        }

        if (CurrentDocument.Layers.Count == 0)
        {
            CurrentDocument.Layers.Add(new LayerNode { Name = "Layer 1" });
        }

        ActiveLayer = CurrentDocument.Layers[0];
        return ActiveLayer;
    }

    private void SyncLayersCollection()
    {
        Layers.Clear();
        foreach (var layer in CurrentDocument.Layers)
        {
            Layers.Add(layer);
        }

        ActiveLayer ??= Layers.FirstOrDefault();
    }

    private void LoadBuiltInAndExternalPlugins()
    {
        _pluginHost.RegisterPlugin(new SampleVectorerPlugin());
        var pluginsFolder = Path.Combine(AppContext.BaseDirectory, "plugins");
        _pluginHost.LoadPluginsFromDirectory(pluginsFolder);

        foreach (var shader in _pluginHost.ShaderProviders)
        {
            _shaderRegistry.Register(shader);
        }

        foreach (var menu in _pluginHost.MenuContributions)
        {
            PluginMenus.Add(menu);
        }

        foreach (var tool in _pluginHost.ToolbarContributions)
        {
            PluginToolbarItems.Add(tool);
        }
    }

    private static SvgDocument CreateInitialDemoDocument()
    {
        var doc = new SvgDocument
        {
            Title = "Vectorer Starter Canvas",
            Width = 860f,
            Height = 540f,
            ViewBox = new SKRect(0, 0, 860, 540),
            BackgroundColor = new SKColor(22, 27, 34)
        };

        var baseLayer = new LayerNode { Name = "Base Shapes Layer" };
        var hex = PolygonShapeNode.CreateRegularConvex(new SKPoint(230, 270), 105f, sides: 6);
        hex.Style.Fill = new SolidColorFill(new SKColor(56, 189, 248));
        hex.Style.Stroke = new StrokeStyle(new SKColor(224, 242, 254), 3f);
        baseLayer.Children.Add(hex);

        var arc = new ArcShapeNode
        {
            Name = "Textured Sector",
            Center = new SKPoint(620, 270),
            RadiusX = 105f,
            RadiusY = 105f,
            StartAngleDegrees = -45f,
            SweepAngleDegrees = 270f,
            CloseMode = ArcCloseMode.PieSlice
        };
        arc.Style.Fill = new TextureFill(
            TexturePatternKind.Checkerboard,
            new SKColor(52, 211, 153),
            new SKColor(6, 78, 59),
            Scale: 20f);
        arc.Style.Stroke = new StrokeStyle(new SKColor(167, 243, 208), 3f);
        baseLayer.Children.Add(arc);

        var blendLayer = new LayerNode
        {
            Name = "Overlay & Shader Layer",
            BlendMode = LayerBlendMode.Overlay
        };

        var star = PolygonShapeNode.CreateConcaveStar(new SKPoint(425, 270), outerRadius: 120f, innerRadius: 52f, pointsCount: 7);
        star.ShaderEffect = new ShaderEffectDescriptor(SampleVectorerPlugin.SunsetWaveShaderId);
        star.Style.Stroke = new StrokeStyle(new SKColor(254, 240, 138), 2.5f);
        blendLayer.Children.Add(star);

        doc.Layers.Add(baseLayer);
        doc.Layers.Add(blendLayer);
        return doc;
    }
}
