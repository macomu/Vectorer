using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Tools;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed record ToolItem(DrawingTool Tool, string Title, string Icon, string Hint)
{
    public override string ToString() => Hint;
}

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<ToolItem> Tools { get; } = [
        new(DrawingTool.Select, "選択", "select", "選択・移動 (V / S)"),
        new(DrawingTool.Node, "ノード", "node", "ノード編集 (N / A)"),
        new(DrawingTool.Rectangle, "四角形", "rectangle", "四角形・角丸 (R)"),
        new(DrawingTool.Ellipse, "楕円", "ellipse", "円・楕円 (E)"),
        new(DrawingTool.Arc, "円弧", "arc", "円弧・扇形"),
        new(DrawingTool.Polygon, "多角形", "polygon", "正多角形 (Y)"),
        new(DrawingTool.Star, "星形", "star", "星形"),
        new(DrawingTool.Freehand, "鉛筆", "pencil", "フリーハンド (P)"),
        new(DrawingTool.Bezier, "ベジェ", "bezier", "ベジェペン (B)"),
        new(DrawingTool.Text, "テキスト", "text", "テキスト (T)"),
        new(DrawingTool.Pan, "パン", "hand", "視点移動 (H) / Space + ドラッグ / 中ボタン")];

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
    public string SelectionSummary => $"選択: {_selectedNodes.Count} 個";
    public string ZoomLabel => $"{Zoom:P0}";
    public string ToolHint => ActiveTool switch
    {
        DrawingTool.Select => "Shift: 複数選択 · ドラッグ: 移動/範囲選択 · ハンドル: 拡縮 · 上の丸: 回転",
        DrawingTool.Pan => "ドラッグ: 視点移動 · Ctrl + ホイール: ズーム",
        DrawingTool.Freehand => "ドラッグ: 自由線 · Esc: キャンセル",
        DrawingTool.Node => "点・ハンドルをドラッグ · Shift: 複数選択 · ダブルクリック: 点追加",
        DrawingTool.Bezier => "クリック: 頂点 · ドラッグ: 制御点 · Enter: 確定 · 始点クリック: 閉じる",
        DrawingTool.Text => "クリック: テキスト入力 · Ctrl+Enter: 確定 · Esc: 取消",
        DrawingTool.Polygon or DrawingTool.Star => "中心からドラッグして描画 · Esc: キャンセル",
        _ => "ドラッグ: 描画 · Shift: 縦横比固定 · Alt: 中心から描画 · Esc: キャンセル"
    };

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

    public void BeginInteraction(SKPoint point, bool shift) => CanvasTool.Begin(point, shift);
    public void UpdateInteraction(SKPoint point, bool shift, bool alt)
    {
        CursorPosition = $"{point.X:0.0}, {point.Y:0.0}";
        CanvasTool.Move(point, shift, alt);
    }
    public void EndInteraction() => CanvasTool.End();
    public void CancelInteraction() => CanvasTool.Cancel();

    [RelayCommand] private void FinishBezier() { if (CanvasTool is BezierPenTool tool) tool.Finish(); }
    [RelayCommand] private void CloseBezier() { if (CanvasTool is BezierPenTool tool) tool.Finish(true); }
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
