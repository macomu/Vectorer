using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Localization;
using Vectorer.App.Tools;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed class ToolItem : ObservableObject
{
    private readonly AppLocalization _l10n;
    private readonly string _titleKey;
    private readonly string _hintKey;

    public DrawingTool Tool { get; }
    public string Icon { get; }
    public string Title => _l10n.Get(_titleKey);
    public string Hint => _l10n.Get(_hintKey);

    public ToolItem(DrawingTool tool, string titleKey, string icon, string hintKey, AppLocalization l10n)
    {
        Tool = tool;
        _titleKey = titleKey;
        Icon = icon;
        _hintKey = hintKey;
        _l10n = l10n;
    }

    public void NotifyLocalizationChanged()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Hint));
    }

    public override string ToString() => Hint;
}

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<ToolItem>? _tools;
    public IReadOnlyList<ToolItem> Tools => _tools ??= [
        new(DrawingTool.Select, nameof(Strings.Tool_Select_Title), "select", nameof(Strings.Tool_Select_Hint), L10n),
        new(DrawingTool.Node, nameof(Strings.Tool_Node_Title), "node", nameof(Strings.Tool_Node_Hint), L10n),
        new(DrawingTool.Rectangle, nameof(Strings.Tool_Rectangle_Title), "rectangle", nameof(Strings.Tool_Rectangle_Hint), L10n),
        new(DrawingTool.Ellipse, nameof(Strings.Tool_Ellipse_Title), "ellipse", nameof(Strings.Tool_Ellipse_Hint), L10n),
        new(DrawingTool.Arc, nameof(Strings.Tool_Arc_Title), "arc", nameof(Strings.Tool_Arc_Hint), L10n),
        new(DrawingTool.Polygon, nameof(Strings.Tool_Polygon_Title), "polygon", nameof(Strings.Tool_Polygon_Hint), L10n),
        new(DrawingTool.Star, nameof(Strings.Tool_Star_Title), "star", nameof(Strings.Tool_Star_Hint), L10n),
        new(DrawingTool.Freehand, nameof(Strings.Tool_Freehand_Title), "pencil", nameof(Strings.Tool_Freehand_Hint), L10n),
        new(DrawingTool.Bezier, nameof(Strings.Tool_Bezier_Title), "bezier", nameof(Strings.Tool_Bezier_Hint), L10n),
        new(DrawingTool.Text, nameof(Strings.Tool_Text_Title), "text", nameof(Strings.Tool_Text_Hint), L10n),
        new(DrawingTool.Pan, nameof(Strings.Tool_Pan_Title), "hand", nameof(Strings.Tool_Pan_Hint), L10n)];

    [ObservableProperty] private ToolItem? _activeToolItem;
    [ObservableProperty] private double _zoom = 1;
    [ObservableProperty] private bool _showGrid;
    [ObservableProperty] private bool _snapToGrid;
    [ObservableProperty] private decimal _cornerRadius;
    [ObservableProperty] private decimal _polygonSides = 5;
    [ObservableProperty] private decimal _innerRatio = 0.45m;
    [ObservableProperty] private decimal _arcStart;
    [ObservableProperty] private decimal _arcSweep = 270;
    [ObservableProperty] private ArcCloseMode _arcMode = ArcCloseMode.PieSlice;
    [ObservableProperty] private string _fillColor = "#388DFF";
    [ObservableProperty] private string _strokeColor = "#E2E8F0";
    [ObservableProperty] private decimal? _strokeWidth = 2;
    [ObservableProperty] private string? _strokeDash = "実線";
    [ObservableProperty] private decimal _outerRadius = 100;
    [ObservableProperty] private decimal _polygonAngle = -90;
    [ObservableProperty] private decimal _smoothing = 35;
    [ObservableProperty] private bool _closeFreehand;
    [ObservableProperty] private bool _bezierPolyline;
    [ObservableProperty] private decimal _simplifyTolerance = 1;
    [ObservableProperty] private string _cursorPosition = "0, 0";

    public DrawingTool ActiveTool => ActiveToolItem?.Tool ?? DrawingTool.Select;
    public bool IsRectangleTool => ActiveTool == DrawingTool.Rectangle || (SelectedNodes.Count == 1 ? SelectedNodes[0] : null) is RectangleShapeNode;
    public bool IsPolygonTool => ActiveTool is DrawingTool.Polygon or DrawingTool.Star;
    public bool IsStarTool => ActiveTool == DrawingTool.Star;
    public bool IsArcTool => ActiveTool == DrawingTool.Arc;
    public bool IsNodeTool => ActiveTool == DrawingTool.Node;
    public bool IsBezierTool => ActiveTool == DrawingTool.Bezier;
    public bool IsFreehandTool => ActiveTool == DrawingTool.Freehand;
    public bool IsTextTool => ActiveTool == DrawingTool.Text;
    public IReadOnlyList<ArcCloseMode> ArcModes { get; } = Enum.GetValues<ArcCloseMode>();
    public string SelectionSummary => L10n.Format(new LocalizedMessage(nameof(Strings.Status_SelectionSummary), _selectedNodes.Count));
    public string ZoomLabel => $"{Zoom:P0}";
    public string ToolHint => L10n.Get(ActiveTool switch
    {
        DrawingTool.Select => nameof(Strings.ToolHint_Select),
        DrawingTool.Pan => nameof(Strings.ToolHint_Pan),
        DrawingTool.Freehand => nameof(Strings.ToolHint_Freehand),
        DrawingTool.Node => nameof(Strings.ToolHint_Node),
        DrawingTool.Bezier => nameof(Strings.ToolHint_Bezier),
        DrawingTool.Text => nameof(Strings.ToolHint_Text),
        DrawingTool.Polygon or DrawingTool.Star => nameof(Strings.ToolHint_Polygon),
        _ => nameof(Strings.ToolHint_Shape)
    });

    private ICanvasTool? _canvasTool;
    public ICanvasTool CanvasTool => _canvasTool ??= new SelectionTool(this);
    public ShapeNode? PreviewShape => CanvasTool.PreviewShape;
    public SKMatrix PreviewParent => CanvasTool.PreviewParent;
    public SKRect? Marquee => CanvasTool.Marquee;
    public bool IsInteracting => CanvasTool.IsInteracting;

    partial void OnActiveToolItemChanged(ToolItem? value)
    {
        _canvasTool?.Cancel();
        _canvasTool = ActiveTool switch
        {
            DrawingTool.Select => new SelectionTool(this),
            DrawingTool.Pan => new PanTool(),
            DrawingTool.Node => new NodeEditTool(this),
            DrawingTool.Bezier => new BezierPenTool(this),
            DrawingTool.Text => new TextTool(this),
            _ => new ShapeDrawingTool(this)
        };
        foreach (var name in new[] { nameof(ActiveTool), nameof(IsRectangleTool), nameof(IsPolygonTool), nameof(IsStarTool), nameof(IsArcTool), nameof(IsNodeTool), nameof(IsBezierTool), nameof(IsFreehandTool), nameof(IsTextTool), nameof(ToolHint) })
            OnPropertyChanged(name);
        NotifyPrimaryInputs();
        RequestRedraw();
    }
    partial void OnZoomChanged(double value) { OnPropertyChanged(nameof(ZoomLabel)); RequestRedraw(); }
    partial void OnShowGridChanged(bool value) => RequestRedraw();
    public void ChooseTool(DrawingTool tool) => ActiveToolItem = Tools.First(t => t.Tool == tool);

    public SKRect? GetSelectionBounds()
    {
        SKRect? bounds = null;
        foreach (var (node, parent) in GetEditableNodes().Where(e => _selectedNodes.Contains(e.Node)))
        {
            using var path = CanvasGeometry.BuildWorldPath(node, parent);
            bounds = bounds is { } b ? SKRect.Union(b, path.TightBounds) : path.TightBounds;
        }
        return bounds;
    }

    public void BeginInteraction(SKPoint point, bool shift, bool forceNew = false)
    {
        if (CanvasTool is ShapeDrawingTool shapes) shapes.Begin(point, shift, forceNew);
        else CanvasTool.Begin(point, shift);
        SyncGestureSafety();
    }
    public void UpdateInteraction(SKPoint point, bool shift, bool alt)
    {
        CursorPosition = $"{point.X:0.0}, {point.Y:0.0}";
        CanvasTool.Move(point, shift, alt);
        SyncGestureSafety();
    }
    public void EndInteraction()
    {
        CanvasTool.End();
        SyncGestureSafety();
    }
    public void CancelInteraction()
    {
        CanvasTool.Cancel();
        SyncGestureSafety();
    }

    [RelayCommand] private void FinishBezier() { if (CanvasTool is BezierPenTool tool) tool.Finish(); SyncGestureSafety(); }
    [RelayCommand] private void CloseBezier() { if (CanvasTool is BezierPenTool tool) tool.Finish(true); SyncGestureSafety(); }
    [RelayCommand] private void AddNode() { if (CanvasTool is NodeEditTool tool) tool.AddMidpoint(); }
    [RelayCommand] private void DeleteNode() { if (CanvasTool is NodeEditTool tool) tool.DeleteNodes(); }
    [RelayCommand] private void CornerNode() { if (CanvasTool is NodeEditTool tool) tool.SetKind(PathNodeKind.Corner); }
    [RelayCommand] private void SmoothNode() { if (CanvasTool is NodeEditTool tool) tool.SetKind(PathNodeKind.Smooth); }
    [RelayCommand] private void SymmetricNode() { if (CanvasTool is NodeEditTool tool) tool.SetKind(PathNodeKind.Symmetric); }
    [RelayCommand] private void StraightSegment() { if (CanvasTool is NodeEditTool tool) tool.SetCurved(false); }
    [RelayCommand] private void CurvedSegment() { if (CanvasTool is NodeEditTool tool) tool.SetCurved(true); }
    [RelayCommand] private void TogglePathClosed() => EditSelectedPaths(false);
    [RelayCommand] private void CutPath() { if (CanvasTool is NodeEditTool tool) tool.CutSelected(); }
    [RelayCommand] private void JoinPath() { if (SelectedNodes.Count > 1) JoinSelectedPaths(); else if (CanvasTool is NodeEditTool tool) tool.JoinSelected(); }
    [RelayCommand] private void SimplifyPath() => EditSelectedPaths(true);

    public static float Distance(SKPoint a, SKPoint b) => MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
    public static SKPoint[] GetHandles(SKRect b) => [new(b.Left,b.Top), new(b.MidX,b.Top), new(b.Right,b.Top), new(b.Right,b.MidY), new(b.Right,b.Bottom), new(b.MidX,b.Bottom), new(b.Left,b.Bottom), new(b.Left,b.MidY)];

    [RelayCommand] private void DeleteSelection()
    {
        if (_canvasTool is NodeEditTool tool) tool.DeleteNodes(); else DeleteSelectedObjects();
    }
    public void DeleteSelectedObjects()
    {
        if (IsInteracting) return;
        var changes = GetEditableEntries()
            .Where(e => !e.Node.IsLocked && _selectedNodes.Contains(e.Node)).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand("Delete selection",
            () => { foreach (var c in changes) c.Container.Children.Remove(c.Node); },
            () => { foreach (var c in changes) c.Container.Children.Insert(c.Index, c.Node); }));
        SelectNodes([]);
    }
    [RelayCommand] private void ZoomIn() => Zoom = Math.Clamp(Zoom * 1.2, 0.1, 32);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Clamp(Zoom / 1.2, 0.1, 32);
    [RelayCommand] private void ResetZoom() => Zoom = 1;

    [RelayCommand] private void ObjectToPath() => ConvertSelectionToPaths(false);
    [RelayCommand] private void StrokeToPath() => ConvertSelectionToPaths(true);
    private void ConvertSelectionToPaths(bool strokeOnly)
    {
        if (IsInteracting) return;
        var replacements = GetEditableEntries()
            .Where(e => !e.Node.IsLocked && _selectedNodes.Contains(e.Node) && e.Node is ShapeNode)
            .Select(e => (e.Container, e.Node, e.Index, Path: ShapePathConverter.Convert((ShapeNode)e.Node, strokeOnly)))
            .Where(e => e.Path is not null).ToArray();
        if (replacements.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand(strokeOnly ? "Stroke to path" : "Object to path",
            () => { foreach (var r in replacements) r.Container.Children[r.Index] = r.Path!; },
            () => { foreach (var r in replacements) r.Container.Children[r.Index] = r.Node; }));
        SelectNodes(replacements.Select(r => r.Path!));
    }
}
