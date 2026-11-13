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
using Vectorer.App.Localization;
using Vectorer.App.Services;
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
    private (LayerNode Node, string Name, LayerBlendMode Blend)[] _layerDisplayState = [];

    [ObservableProperty]
    private SvgDocument _currentDocument;

    [ObservableProperty]
    private LayerNode? _activeLayer;

    [ObservableProperty]
    private int _renderVersion;

    private LocalizedMessage _status = new(nameof(Strings.Status_Ready));

    public string StatusMessage
    {
        get => L10n.Format(_status);
        set => SetStatus(LocalizedMessage.Literal(value));
    }

    public void SetStatus(LocalizedMessage message)
    {
        if (Equals(_status, message)) return;
        _status = message;
        OnPropertyChanged(nameof(StatusMessage));
    }

    public void SetStatus(string key, params object?[] arguments) =>
        SetStatus(new LocalizedMessage(key, arguments));

    public IDocumentRenderer DocumentRenderer { get; }
    public AppLocalization L10n { get; }
    public EditorNaming Naming { get; }
    public EditorUserSettingsSession SettingsSession { get; }
    public LanguageChangeSafety LanguageSafety { get; } = new();
    public LanguageChangeCoordinator LanguageCoordinator { get; }
    public bool HasPendingLanguage => LanguageCoordinator.PendingLanguage is not null;
    public string PendingLanguageNotice => HasPendingLanguage ? L10n.Status_LanguageChangePending : string.Empty;

    public ObservableCollection<LayerNode> Layers { get; } = [];
    public ObservableCollection<PluginMenuItem> PluginMenus { get; } = [];
    public ObservableCollection<PluginToolbarItem> PluginToolbarItems { get; } = [];

    public IReadOnlyList<SceneNode> SelectedNodes => _selectedNodes;

    public MainWindowViewModel(AppLocalization? localization = null, EditorUserSettingsSession? settingsSession = null)
    {
        L10n = localization ?? new AppLocalization();
        Naming = new EditorNaming(L10n);
        SettingsSession = settingsSession ?? new EditorUserSettingsSession(
            postNotification: action =>
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    action();
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(action);
                }
            });
        SettingsSession.SaveFailed += (_, ex) =>
        {
            OnPropertyChanged(nameof(HasSettingsSaveError));
            SetStatus(nameof(Strings.Status_SettingsSaveFailed), ex.Message);
        };
        SettingsSession.SaveSucceeded += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSettingsSaveError));
            if (Equals(_status.Key, nameof(Strings.Status_SettingsSaveFailed)))
            {
                SetStatus(nameof(Strings.Status_Ready));
            }
        };
        LanguageCoordinator = new LanguageChangeCoordinator(
            L10n,
            canApplyNow: () => !IsInteracting,
            onCultureApplied: (_, _) => OnLanguageApplied(),
            safety: LanguageSafety,
            scheduleFlush: action => Avalonia.Threading.Dispatcher.UIThread.Post(action),
            settingsSession: SettingsSession);
        LanguageCoordinator.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LanguageChangeCoordinator.PendingLanguage))
            {
                OnPropertyChanged(nameof(HasPendingLanguage));
                OnPropertyChanged(nameof(PendingLanguageNotice));
            }
        };

        _undoRedoService = new UndoRedoService();
        _booleanService = new PathBooleanService();
        _svgSerializer = new SvgSerializer();
        _shaderRegistry = new ShaderEffectRegistry();
        DocumentRenderer = new SkiaDocumentRenderer(_shaderRegistry);
        _pngExportService = new PngExportService(DocumentRenderer);
        _pluginHost = new PluginHostService();
        _pluginHost.NotifyUiCultureChanged(L10n.UiCulture);

        _currentDocument = Naming.CreateDefaultDocument();
        SyncLayersCollection();

        LoadBuiltInAndExternalPlugins();
        ActiveToolItem = Tools[0];
        SyncSelectionEditors();
    }

    public void ExecuteCommand(IDocumentCommand command)
    {
        _undoRedoService.ExecuteCommand(command);
        NotifyDocumentState();
        SyncLayersCollection();
        SyncSelectionEditors();
        RequestRedraw();
    }

    public void SelectNodes(IEnumerable<SceneNode> nodes)
    {
        FinishPaintEdit();
        var selection = nodes.Distinct().ToList();
        _selectedNodes.Clear();
        _selectedNodes.AddRange(selection);
        if (_canvasTool is Vectorer.App.Tools.NodeEditTool nodesTool) nodesTool.Synchronize();
        SyncSelectionEditors();
        SyncSceneTree();
        OnPropertyChanged(nameof(SelectionSummary));
        RequestRedraw();
        SetStatus(nameof(Strings.Status_SelectedItems), _selectedNodes.Count);
    }

    public void RequestRedraw() => RequestRedraw(true);
    public void RequestRedraw(bool invalidateScene)
    {
        if (invalidateScene) _snapCandidates = null;
        ValidateScope();
        SyncNodeCoordinates();
        MergeLayerDownCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ScopePath));
        OnPropertyChanged(nameof(IsInGroup));
        RenderVersion++;
        SyncGestureSafety();
    }

    public void SyncGestureSafety() => LanguageSafety.SetGestureActive(IsInteracting);

    [RelayCommand]
    private void AddConvexPolygon()
    {
        var layer = EnsureActiveLayer();
        var hex = PolygonShapeNode.CreateRegularConvex(new SKPoint(220, 220), 85f, sides: 6);
        hex.Name = Naming.PolygonName(6);
        hex.Style.Fill = new SolidColorFill(new SKColor(56, 189, 248));
        hex.Style.Stroke = new StrokeStyle(new SKColor(15, 23, 42), 3f);

        ExecuteCommand(new AddNodeCommand(layer, hex, "Add Convex Hexagon"));
        SelectNodes([hex]);
        SetStatus(nameof(Strings.Status_AddedHexagon));
    }

    [RelayCommand]
    private void AddConcavePolygon()
    {
        var layer = EnsureActiveLayer();
        var star = PolygonShapeNode.CreateConcaveStar(new SKPoint(420, 240), outerRadius: 95f, innerRadius: 42f, pointsCount: 5);
        star.Name = Naming.StarName(5);
        star.Style.Fill = new TextureFill(
            TexturePatternKind.Checkerboard,
            new SKColor(250, 204, 21),
            new SKColor(234, 88, 12),
            Scale: 18f);
        star.Style.Stroke = new StrokeStyle(new SKColor(30, 41, 59), 2.5f);

        ExecuteCommand(new AddNodeCommand(layer, star, "Add Concave Star"));
        SelectNodes([star]);
        SetStatus(nameof(Strings.Status_AddedStar));
    }

    [RelayCommand]
    private void AddCircle()
    {
        var layer = EnsureActiveLayer();
        var circle = new EllipseShapeNode
        {
            Name = Naming.CircleName(),
            Center = new SKPoint(320, 260),
            RadiusX = 80f,
            RadiusY = 80f
        };
        circle.Style.Fill = new SolidColorFill(new SKColor(168, 85, 247, 220));
        circle.Style.Stroke = new StrokeStyle(new SKColor(243, 232, 255), 3f);

        ExecuteCommand(new AddNodeCommand(layer, circle, "Add Circle"));
        SelectNodes([circle]);
        SetStatus(nameof(Strings.Status_AddedCircle));
    }

    [RelayCommand]
    private void AddArc()
    {
        var layer = EnsureActiveLayer();
        var arc = new ArcShapeNode
        {
            Name = Naming.PieArcName(),
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
        SetStatus(nameof(Strings.Status_AddedArc));
    }

    [RelayCommand]
    private void BooleanUnionActiveLayerShapes()
    {
        ApplyBooleanOnSelection(BooleanOperationType.Union);
    }

    [RelayCommand]
    private void BooleanDifferenceActiveLayerShapes()
    {
        ApplyBooleanOnSelection(BooleanOperationType.Difference);
    }

    [RelayCommand] private void BooleanIntersection() => ApplyBooleanOnSelection(BooleanOperationType.Intersection);
    [RelayCommand] private void BooleanXor() => ApplyBooleanOnSelection(BooleanOperationType.Xor);
    [RelayCommand] private void BooleanReverseDifference() => ApplyBooleanOnSelection(BooleanOperationType.ReverseDifference);

    [RelayCommand]
    private void ToggleGaussianBlurOnActiveLayer()
    {
        var layer = EnsureActiveLayer();
        var before = layer.Filter;
        var after = before is { HasActiveFilter: true } ? null : new FilterSettings(BlurSigmaX: 6f, BlurSigmaY: 6f);
        ExecuteCommand(new DelegateDocumentCommand("Layer blur", () => layer.Filter = after, () => layer.Filter = before));
        RequestRedraw();
        SetStatus(layer.Filter is not null ? nameof(Strings.Status_BlurApplied) : nameof(Strings.Status_BlurCleared), layer.Name);
    }

    [RelayCommand]
    private void CycleActiveLayerBlendMode()
    {
        var layer = EnsureActiveLayer();
        var modes = Enum.GetValues<LayerBlendMode>();
        var nextIndex = ((int)layer.BlendMode + 1) % modes.Length;
        var before = layer.BlendMode;
        var after = modes[nextIndex];
        ExecuteCommand(new DelegateDocumentCommand("Layer blend mode", () => layer.BlendMode = after, () => layer.BlendMode = before));
        RequestRedraw();
        SetStatus(nameof(Strings.Status_BlendChanged), layer.Name, layer.BlendMode);
    }

    [RelayCommand]
    private void Undo()
    {
        FinishPaintEdit();
        if (IsInteracting || IsBusy) return;
        if (_undoRedoService.CanUndo)
        {
            _undoRedoService.Undo();
            NotifyDocumentState();
            SelectNodes([]);
            SyncLayersCollection();
            RequestRedraw();
            SetStatus(nameof(Strings.Status_Undo));
        }
    }

    [RelayCommand]
    private void Redo()
    {
        FinishPaintEdit();
        if (IsInteracting || IsBusy) return;
        if (_undoRedoService.CanRedo)
        {
            _undoRedoService.Redo();
            NotifyDocumentState();
            SelectNodes([]);
            SyncLayersCollection();
            RequestRedraw();
            SetStatus(nameof(Strings.Status_Redo));
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
        SetStatus(nameof(Strings.Status_PluginExecuted), item.Id);
    }

    private void ApplyBooleanOnSelection(BooleanOperationType op)
    {
        if (IsInteracting) return;
        var selected = GetEditableEntries().Where(e => SelectedNodes.Contains(e.Node)).ToArray();
        if (selected.Length < 2 || selected.Length != SelectedNodes.Count || selected.Any(e => e.Node is not ShapeNode)
            || selected.Select(e => e.Container).Distinct().Count() != 1)
        { SetStatus(nameof(Strings.Status_NeedShapes)); return; }
        var layer = selected[0].Container;
        var shapes = selected.Select(e => (ShapeNode)e.Node).ToList();
        var positions = selected.Select(e => (e.Node, e.Index)).ToArray();
        var combined = _booleanService.CombineMany(shapes, op);
        if (combined is null)
        {
            SetStatus(nameof(Strings.Status_BooleanEmpty), op);
            return;
        }

        var hasGeometry = !string.IsNullOrWhiteSpace(combined.SvgPathData);
        ExecuteCommand(new DelegateDocumentCommand(
            $"Boolean {op}",
            execute: () =>
            {
                foreach (var shape in shapes) layer.Children.Remove(shape);
                if (hasGeometry) layer.Children.Insert(positions[0].Index, combined);
            },
            undo: () =>
            {
                layer.Children.Remove(combined);
                foreach (var position in positions) layer.Children.Insert(position.Index, position.Node);
            }));

        SelectNodes(hasGeometry ? [combined] : []);
        SetStatus(nameof(Strings.Status_BooleanApplied), op, shapes[0].Name, shapes[1].Name);
    }

    private LayerNode EnsureActiveLayer()
    {
        if (ActiveLayer is not null)
        {
            return ActiveLayer;
        }

        if (CurrentDocument.Layers.Count == 0)
        {
            CurrentDocument.Layers.Add(new LayerNode { Name = Naming.Layer(1) });
        }

        ActiveLayer = CurrentDocument.Layers[0];
        return ActiveLayer;
    }

    private void SyncLayersCollection()
    {
        var previousActiveLayer = ActiveLayer;
        var display = CurrentDocument.Layers.Select(l => (l, l.Name, l.BlendMode)).ToArray();
        if (_layerDisplayState.SequenceEqual(display))
        {
            if (ActiveLayer is null || !Layers.Contains(ActiveLayer)) ActiveLayer = Layers.FirstOrDefault();
            SyncSceneTree();
            return;
        }
        _layerDisplayState = display;
        Layers.Clear();
        foreach (var layer in CurrentDocument.Layers)
        {
            Layers.Add(layer);
        }

        ActiveLayer = previousActiveLayer is not null && Layers.Contains(previousActiveLayer)
            ? previousActiveLayer : Layers.FirstOrDefault();
        SyncSceneTree();
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
            PluginMenus.Add(new PluginMenuItem(menu));
        }

        foreach (var tool in _pluginHost.ToolbarContributions)
        {
            PluginToolbarItems.Add(new PluginToolbarItem(tool));
        }
    }

    [RelayCommand]
    private async Task OpenAboutDialogAsync()
    {
        if (FileDialogs is null)
        {
            return;
        }

        await FileDialogs.ShowAboutDialogAsync(AppMetadata.Current);
    }

}
