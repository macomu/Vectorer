using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
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
        new(DrawingTool.Rectangle, "四角形", "rectangle", "四角形・角丸 (R)"),
        new(DrawingTool.Ellipse, "楕円", "ellipse", "円・楕円 (E)"),
        new(DrawingTool.Arc, "円弧", "arc", "円弧・扇形"),
        new(DrawingTool.Polygon, "多角形", "polygon", "正多角形 (Y)"),
        new(DrawingTool.Star, "星形", "star", "星形"),
        new(DrawingTool.Freehand, "鉛筆", "pencil", "フリーハンド (P)"),
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
    [ObservableProperty] private decimal _strokeWidth = 2;
    [ObservableProperty] private string _cursorPosition = "0, 0";

    public DrawingTool ActiveTool => ActiveToolItem?.Tool ?? DrawingTool.Select;
    public bool IsRectangleTool => ActiveTool == DrawingTool.Rectangle;
    public bool IsPolygonTool => ActiveTool is DrawingTool.Polygon or DrawingTool.Star;
    public bool IsStarTool => ActiveTool == DrawingTool.Star;
    public bool IsArcTool => ActiveTool == DrawingTool.Arc;
    public IReadOnlyList<ArcCloseMode> ArcModes { get; } = Enum.GetValues<ArcCloseMode>();
    public string SelectionSummary => $"選択: {_selectedNodes.Count} 個";
    public string ZoomLabel => $"{Zoom:P0}";
    public string ToolHint => ActiveTool switch
    {
        DrawingTool.Select => "Shift: 複数選択 · ドラッグ: 移動/範囲選択 · ハンドル: 拡縮 · 上の丸: 回転",
        DrawingTool.Pan => "ドラッグ: 視点移動 · Ctrl + ホイール: ズーム",
        DrawingTool.Freehand => "ドラッグ: 自由線 · Esc: キャンセル",
        DrawingTool.Polygon or DrawingTool.Star => "中心からドラッグして描画 · Esc: キャンセル",
        _ => "ドラッグ: 描画 · Shift: 縦横比固定 · Alt: 中心から描画 · Esc: キャンセル"
    };

    public ShapeNode? PreviewShape { get; private set; }
    public SKMatrix PreviewParent { get; private set; } = SKMatrix.CreateIdentity();
    public SKRect? Marquee { get; private set; }
    public bool IsInteracting => _gesture != Gesture.None;
    private enum Gesture { None, Draw, Move, Marquee, Scale, Rotate }
    private Gesture _gesture;
    private SKPoint _start;
    private SKRect _selectionBounds;
    private SKPoint _anchor;
    private bool _hasDragged;
    private LayerNode? _drawingLayer;
    private readonly List<SKPoint> _freehandPoints = [];
    private readonly List<(SceneNode Node, SKMatrix Parent, SKMatrix Before)> _transforms = [];
    private List<SceneNode> _selectionBefore = [];

    partial void OnActiveToolItemChanged(ToolItem? value)
    {
        CancelInteraction();
        foreach (var name in new[] { nameof(ActiveTool), nameof(IsRectangleTool), nameof(IsPolygonTool), nameof(IsStarTool), nameof(IsArcTool), nameof(ToolHint) })
            OnPropertyChanged(name);
    }
    partial void OnZoomChanged(double value) { OnPropertyChanged(nameof(ZoomLabel)); RequestRedraw(); }
    partial void OnShowGridChanged(bool value) => RequestRedraw();
    public void ChooseTool(DrawingTool tool) => ActiveToolItem = Tools.First(t => t.Tool == tool);

    public SKRect? GetSelectionBounds()
    {
        SKRect? bounds = null;
        foreach (var (node, parent) in CanvasGeometry.EditableNodes(CurrentDocument).Where(e => _selectedNodes.Contains(e.Node)))
        {
            using var path = CanvasGeometry.BuildWorldPath(node, parent);
            bounds = bounds is { } b ? SKRect.Union(b, path.TightBounds) : path.TightBounds;
        }
        return bounds;
    }

    public void BeginInteraction(SKPoint point, bool shift)
    {
        CancelInteraction();
        _start = SnapToGrid && ActiveTool != DrawingTool.Select ? CanvasGeometry.Snap(point) : point;
        _hasDragged = false;
        _selectionBefore = _selectedNodes.ToList();
        if (ActiveTool == DrawingTool.Pan) return;
        if (ActiveTool != DrawingTool.Select)
        {
            _drawingLayer = EnsureActiveLayer();
            if (_drawingLayer.IsLocked || !_drawingLayer.IsVisible || !_drawingLayer.Transform.TryInvert(out _))
            { StatusMessage = "描画先レイヤーが非表示・ロック中、または変換できません。"; return; }
            PreviewParent = _drawingLayer.Transform;
            _freehandPoints.Clear();
            _freehandPoints.Add(_start);
            _gesture = Gesture.Draw;
            return;
        }
        var bounds = GetSelectionBounds();
        var tolerance = (float)(7 / Zoom);
        if (bounds is { } selected)
        {
            var handles = GetHandles(selected);
            var handle = Array.FindIndex(handles, h => Distance(h, point) <= tolerance);
            if (handle >= 0)
            {
                _selectionBounds = selected;
                _anchor = handles[(handle + 4) % 8];
                _gesture = Gesture.Scale;
            }
            else if (Distance(new SKPoint(selected.MidX, selected.Top - (float)(24 / Zoom)), point) <= tolerance)
            {
                _selectionBounds = selected;
                _gesture = Gesture.Rotate;
            }
        }
        if (_gesture == Gesture.None)
        {
            var hit = CanvasGeometry.EditableNodes(CurrentDocument).Reverse()
                .FirstOrDefault(e => CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Zoom))).Node;
            if (hit is null)
            {
                if (!shift) SelectNodes([]);
                _gesture = Gesture.Marquee;
            }
            else
            {
                if (shift)
                {
                    SelectNodes(_selectedNodes.Contains(hit) ? _selectedNodes.Where(n => n != hit).ToList() : _selectedNodes.Append(hit).ToList());
                    if (!_selectedNodes.Contains(hit)) return;
                }
                else if (!_selectedNodes.Contains(hit)) SelectNodes([hit]);
                _gesture = Gesture.Move;
            }
        }
        _transforms.Clear();
        _transforms.AddRange(CanvasGeometry.EditableNodes(CurrentDocument).Where(e => _selectedNodes.Contains(e.Node))
            .Select(e => (e.Node, e.Parent, e.Node.Transform)));
    }

    public void UpdateInteraction(SKPoint point, bool shift, bool alt)
    {
        CursorPosition = $"{point.X:0.0}, {point.Y:0.0}";
        if (_gesture == Gesture.None) return;
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
        {
            if (!_hasDragged && Distance(point, _start) < 2 / Zoom) return;
            _hasDragged = true;
        }
        if (SnapToGrid) point = CanvasGeometry.Snap(point);
        switch (_gesture)
        {
            case Gesture.Draw:
                if (ActiveTool == DrawingTool.Freehand)
                {
                    if (Distance(_freehandPoints[^1], point) > 1 / Zoom) _freehandPoints.Add(point);
                    using var path = new SKPath();
                    path.AddPoly(_freehandPoints.ToArray(), false);
                    PreviewShape = new PathShapeNode(path.ToSvgPathData());
                }
                else PreviewShape = CanvasGeometry.CreateShape(ActiveTool, _start, point, shift, alt,
                    (float)CornerRadius, (int)PolygonSides, (float)InnerRatio, (float)ArcStart, (float)ArcSweep, ArcMode);
                PreviewShape.Style.Fill = ActiveTool == DrawingTool.Freehand ? null : new SolidColorFill(SKColor.TryParse(FillColor, out var fill) ? fill : SKColors.CornflowerBlue);
                PreviewShape.Style.Stroke = new StrokeStyle(SKColor.TryParse(StrokeColor, out var stroke) ? stroke : SKColors.White, (float)StrokeWidth);
                if (PreviewParent.TryInvert(out var inverse)) PreviewShape.Transform = inverse;
                break;
            case Gesture.Marquee:
                Marquee = CanvasGeometry.DragBounds(_start, point, false, false);
                var hits = CanvasGeometry.EditableNodes(CurrentDocument).Where(e =>
                {
                    using var path = CanvasGeometry.BuildWorldPath(e.Node, e.Parent);
                    return Marquee.Value.Contains(path.TightBounds);
                }).Select(e => e.Node);
                SelectNodes(shift ? _selectionBefore.Concat(hits).Distinct().ToList() : hits.ToList());
                break;
            default:
                SKMatrix delta;
                if (_gesture == Gesture.Move)
                {
                    var origin = SnapToGrid ? CanvasGeometry.Snap(_start) : _start;
                    delta = SKMatrix.CreateTranslation(point.X - origin.X, point.Y - origin.Y);
                }
                else if (_gesture == Gesture.Rotate)
                {
                    var cx = _selectionBounds.MidX;
                    var cy = _selectionBounds.MidY;
                    var angle = (MathF.Atan2(point.Y - cy, point.X - cx) - MathF.Atan2(_start.Y - cy, _start.X - cx)) * 180 / MathF.PI;
                    if (shift) angle = MathF.Round(angle / 15) * 15;
                    delta = SKMatrix.CreateRotationDegrees(angle, cx, cy);
                }
                else
                {
                    var sx = MathF.Abs(_start.X - _anchor.X) < 0.001f ? 1 : (point.X - _anchor.X) / (_start.X - _anchor.X);
                    var sy = MathF.Abs(_start.Y - _anchor.Y) < 0.001f ? 1 : (point.Y - _anchor.Y) / (_start.Y - _anchor.Y);
                    if (shift) sx = sy = MathF.Abs(sx) > MathF.Abs(sy) ? sx : sy;
                    if (MathF.Abs(sx) < 0.001f || MathF.Abs(sy) < 0.001f) return;
                    delta = SKMatrix.CreateScale(sx, sy, _anchor.X, _anchor.Y);
                }
                foreach (var (node, parent, before) in _transforms)
                    node.Transform = CanvasGeometry.ApplyWorldTransform(before, parent, delta);
                break;
        }
        RequestRedraw();
    }

    public void EndInteraction()
    {
        if (_gesture == Gesture.Draw && PreviewShape is { } shape && _drawingLayer is { } layer)
        {
            using var path = shape.BuildLocalPath();
            if (ActiveTool == DrawingTool.Freehand ? MathF.Max(path.Bounds.Width, path.Bounds.Height) > 1
                : path.Bounds.Width > 1 && path.Bounds.Height > 1)
            {
                ExecuteCommand(new AddNodeCommand(layer, shape));
                SelectNodes([shape]);
            }
        }
        else if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
        {
            var changes = _transforms.Where(t => t.Node.Transform != t.Before).Select(t => (t.Node, t.Before, After: t.Node.Transform)).ToArray();
            if (changes.Length > 0)
                ExecuteCommand(new DelegateDocumentCommand("Transform selection",
                    () => { foreach (var change in changes) change.Node.Transform = change.After; },
                    () => { foreach (var change in changes) change.Node.Transform = change.Before; }));
        }
        ClearGesture();
    }

    public void CancelInteraction()
    {
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
            foreach (var (node, _, before) in _transforms) node.Transform = before;
        if (_gesture == Gesture.Marquee) SelectNodes(_selectionBefore);
        ClearGesture();
    }
    private void ClearGesture() { _gesture = Gesture.None; PreviewShape = null; Marquee = null; _transforms.Clear(); RequestRedraw(); }
    public static float Distance(SKPoint a, SKPoint b) => MathF.Sqrt(MathF.Pow(a.X - b.X, 2) + MathF.Pow(a.Y - b.Y, 2));
    public static SKPoint[] GetHandles(SKRect b) => [new(b.Left,b.Top), new(b.MidX,b.Top), new(b.Right,b.Top), new(b.Right,b.MidY), new(b.Right,b.Bottom), new(b.MidX,b.Bottom), new(b.Left,b.Bottom), new(b.Left,b.MidY)];

    [RelayCommand] private void DeleteSelection()
    {
        if (IsInteracting) return;
        var changes = CurrentDocument.Layers.Where(l => !l.IsLocked && l.IsVisible)
            .SelectMany(l => l.Children.Select((n, i) => (Layer: l, Node: n, Index: i)))
            .Where(e => !e.Node.IsLocked && _selectedNodes.Contains(e.Node)).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand("Delete selection",
            () => { foreach (var c in changes) c.Layer.Children.Remove(c.Node); },
            () => { foreach (var c in changes) c.Layer.Children.Insert(c.Index, c.Node); }));
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
        var replacements = CurrentDocument.Layers.Where(l => l.IsVisible && !l.IsLocked)
            .SelectMany(l => l.Children.Select((n, i) => (Layer: l, Node: n, Index: i)))
            .Where(e => !e.Node.IsLocked && _selectedNodes.Contains(e.Node) && e.Node is ShapeNode)
            .Select(e => (e.Layer, e.Node, e.Index, Path: ShapePathConverter.Convert((ShapeNode)e.Node, strokeOnly)))
            .Where(e => e.Path is not null).ToArray();
        if (replacements.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand(strokeOnly ? "Stroke to path" : "Object to path",
            () => { foreach (var r in replacements) r.Layer.Children[r.Index] = r.Path!; },
            () => { foreach (var r in replacements) r.Layer.Children[r.Index] = r.Node; }));
        SelectNodes(replacements.Select(r => r.Path!));
    }
}
