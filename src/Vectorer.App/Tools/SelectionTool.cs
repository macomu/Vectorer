using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;
using static Vectorer.App.ViewModels.MainWindowViewModel;

namespace Vectorer.App.Tools;

public enum SelectionTransformMode { Scale, Rotate }

public sealed class SelectionTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private readonly ShapeParameterGesture _parameters = new(workspace);
    private SKPoint _pressPoint;
    private enum Gesture { None, Move, Marquee, Scale, Rotate, Skew, MovePivot }
    private Gesture _gesture;
    private SKPoint _start;
    private SKRect _selectionBounds;
    private SKPoint _anchor;
    private SKPoint _pivot;
    private SKPoint? _pivotBefore;
    private int _skewIndex;
    private bool _hasDragged;
    private bool _toggleModeOnClick;
    private bool _resetPivotOnClick;
    private readonly List<(SceneNode Node, SKMatrix Parent, SKMatrix Before)> _transforms = [];
    private List<SceneNode> _selectionBefore = [];
    public SelectionTransformMode TransformMode { get; private set; } = SelectionTransformMode.Scale;
    public SKPoint? CustomPivot { get; private set; }
    public override bool IsInteracting => _gesture != Gesture.None || _parameters.IsActive;

    public static float HandleOffset(double zoom) => (float)(5 / Math.Max(zoom, 0.01));

    public static SKPoint[] GetRotationHandles(SKRect bounds, double zoom)
    {
        var offset = HandleOffset(zoom);
        return
        [
            new(bounds.Left - offset, bounds.Top - offset),
            new(bounds.Right + offset, bounds.Top - offset),
            new(bounds.Right + offset, bounds.Bottom + offset),
            new(bounds.Left - offset, bounds.Bottom + offset)
        ];
    }

    public static SKPoint[] GetSkewHandles(SKRect bounds, double zoom)
    {
        var offset = HandleOffset(zoom);
        return
        [
            new(bounds.MidX, bounds.Top - offset),
            new(bounds.Right + offset, bounds.MidY),
            new(bounds.MidX, bounds.Bottom + offset),
            new(bounds.Left - offset, bounds.MidY)
        ];
    }

    public SKPoint GetPivot(SKRect bounds) => CustomPivot ?? new SKPoint(bounds.MidX, bounds.MidY);

    public void ResetTransformMode()
    {
        if (TransformMode == SelectionTransformMode.Scale && CustomPivot is null) return;
        TransformMode = SelectionTransformMode.Scale;
        CustomPivot = null;
        Workspace.NotifyToolHintChanged();
    }

    private void ToggleTransformMode()
    {
        TransformMode = TransformMode == SelectionTransformMode.Scale
            ? SelectionTransformMode.Rotate
            : SelectionTransformMode.Scale;
        Workspace.NotifyToolHintChanged();
        Workspace.RequestRedraw(false);
    }

    private SceneNode? HitSelectedNode(SKPoint point) =>
        Workspace.GetEditableNodes().Reverse()
            .FirstOrDefault(e => Workspace.SelectedNodes.Contains(e.Node)
                && CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Workspace.Zoom))).Node;

    public override void Begin(SKPoint point, bool shift)
    {
        Cancel();
        _start = point;
        _pressPoint = point;
        _hasDragged = false;
        _toggleModeOnClick = false;
        _resetPivotOnClick = false;
        _pivotBefore = CustomPivot;
        if (TransformMode == SelectionTransformMode.Scale && _parameters.Begin(point)) return;
        _selectionBefore = Workspace.SelectedNodes.ToList();
        var bounds = Workspace.GetSelectionBounds();
        var tolerance = (float)(8 / Workspace.Zoom);
        if (bounds is { } selected)
        {
            if (TransformMode == SelectionTransformMode.Scale)
            {
                var handles = GetHandles(selected);
                var handle = Array.FindIndex(handles, h => Distance(h, point) <= tolerance);
                if (handle >= 0)
                {
                    _selectionBounds = selected;
                    _anchor = handles[(handle + 4) % 8];
                    _gesture = Gesture.Scale;
                }
                else if (Distance(new SKPoint(selected.MidX, selected.Top - (float)(24 / Workspace.Zoom)), point) <= tolerance)
                {
                    _selectionBounds = selected;
                    _pivot = GetPivot(selected);
                    _gesture = Gesture.Rotate;
                }
            }
            else
            {
                var pivot = GetPivot(selected);
                if (Distance(pivot, point) <= tolerance)
                {
                    _gesture = Gesture.MovePivot;
                    if (shift) _resetPivotOnClick = true;
                    else _toggleModeOnClick = HitSelectedNode(point) is not null;
                }
                else
                {
                    var rotationHandles = GetRotationHandles(selected, Workspace.Zoom);
                    var rotationIndex = Array.FindIndex(rotationHandles, h => Distance(h, point) <= tolerance);
                    if (rotationIndex >= 0)
                    {
                        _selectionBounds = selected;
                        _pivot = pivot;
                        _gesture = Gesture.Rotate;
                    }
                    else
                    {
                        var skewHandles = GetSkewHandles(selected, Workspace.Zoom);
                        var skewIndex = Array.FindIndex(skewHandles, h => Distance(h, point) <= tolerance);
                        if (skewIndex >= 0)
                        {
                            _selectionBounds = selected;
                            _pivot = pivot;
                            _skewIndex = skewIndex;
                            _anchor = skewIndex switch
                            {
                                0 => new(selected.MidX, selected.Bottom),
                                1 => new(selected.Left, selected.MidY),
                                2 => new(selected.MidX, selected.Top),
                                _ => new(selected.Right, selected.MidY)
                            };
                            _gesture = Gesture.Skew;
                        }
                    }
                }
            }
        }
        if (_gesture == Gesture.None)
        {
            var hit = Workspace.GetEditableNodes().Reverse()
                .FirstOrDefault(e => CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Workspace.Zoom))).Node;
            if (hit is null)
            {
                if (!shift) Workspace.SelectNodes([]);
                _gesture = Gesture.Marquee;
            }
            else
            {
                if (shift)
                {
                    Workspace.SelectNodes(Workspace.SelectedNodes.Contains(hit) ? Workspace.SelectedNodes.Where(n => n != hit).ToList() : Workspace.SelectedNodes.Append(hit).ToList());
                    if (!Workspace.SelectedNodes.Contains(hit)) return;
                }
                else if (!Workspace.SelectedNodes.Contains(hit))
                {
                    Workspace.SelectNodes([hit]);
                }
                else
                {
                    _toggleModeOnClick = true;
                }
                _gesture = Gesture.Move;
            }
        }
        _transforms.Clear();
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate or Gesture.Skew)
        {
            _transforms.AddRange(Workspace.GetEditableNodes().Where(e => Workspace.SelectedNodes.Contains(e.Node))
                .Select(e => (e.Node, e.Parent, e.Node.Transform)));
        }
    }

    public override void Move(SKPoint point, bool shift, bool alt)
    {
        if (_parameters.IsActive) { if (Distance(point, _pressPoint) > 2 / Workspace.Zoom) _parameters.Move(point); return; }
        if (_gesture == Gesture.None) return;
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate or Gesture.Skew or Gesture.MovePivot)
        {
            if (!_hasDragged && Distance(point, _start) < 2 / Workspace.Zoom) return;
            _hasDragged = true;
            if (_gesture == Gesture.MovePivot && _resetPivotOnClick) return;
        }
        if (Workspace.SnapToGrid && _gesture is not (Gesture.Rotate or Gesture.Skew)) point = Workspace.SnapPoint(point, true);
        switch (_gesture)
        {
            case Gesture.Marquee:
                Marquee = CanvasGeometry.DragBounds(_start, point, false, false);
                var hits = Workspace.GetEditableNodes().Where(e =>
                {
                    using var path = CanvasGeometry.BuildWorldPath(e.Node, e.Parent);
                    return Marquee.Value.Contains(path.TightBounds);
                }).Select(e => e.Node);
                Workspace.SelectNodes(shift ? _selectionBefore.Concat(hits).Distinct().ToList() : hits.ToList());
                break;
            case Gesture.MovePivot:
                CustomPivot = point;
                break;
            default:
                SKMatrix delta;
                if (_gesture == Gesture.Move)
                {
                    var origin = Workspace.SnapToGrid ? CanvasGeometry.Snap(_start) : _start;
                    var dx = point.X - origin.X;
                    var dy = point.Y - origin.Y;
                    delta = SKMatrix.CreateTranslation(dx, dy);
                    if (_pivotBefore is { } p) CustomPivot = new SKPoint(p.X + dx, p.Y + dy);
                }
                else if (_gesture == Gesture.Rotate)
                {
                    var angle = (MathF.Atan2(point.Y - _pivot.Y, point.X - _pivot.X) - MathF.Atan2(_start.Y - _pivot.Y, _start.X - _pivot.X)) * 180 / MathF.PI;
                    if (shift) angle = MathF.Round(angle / 15) * 15;
                    delta = SKMatrix.CreateRotationDegrees(angle, _pivot.X, _pivot.Y);
                }
                else if (_gesture == Gesture.Skew)
                {
                    var anchor = shift ? _pivot : _anchor;
                    if (_skewIndex is 0 or 2)
                    {
                        var h = _start.Y - anchor.Y;
                        if (MathF.Abs(_selectionBounds.Height) < 0.001f || MathF.Abs(h) < 0.001f) return;
                        var skewX = (point.X - _start.X) / h;
                        delta = SKMatrix.Concat(
                            SKMatrix.CreateTranslation(anchor.X, anchor.Y),
                            SKMatrix.Concat(SKMatrix.CreateSkew(skewX, 0), SKMatrix.CreateTranslation(-anchor.X, -anchor.Y)));
                    }
                    else
                    {
                        var w = _start.X - anchor.X;
                        if (MathF.Abs(_selectionBounds.Width) < 0.001f || MathF.Abs(w) < 0.001f) return;
                        var skewY = (point.Y - _start.Y) / w;
                        delta = SKMatrix.Concat(
                            SKMatrix.CreateTranslation(anchor.X, anchor.Y),
                            SKMatrix.Concat(SKMatrix.CreateSkew(0, skewY), SKMatrix.CreateTranslation(-anchor.X, -anchor.Y)));
                    }
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
        Workspace.RequestRedraw(false);
    }

    public override void End()
    {
        if (_parameters.IsActive) { _parameters.End(); return; }
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate or Gesture.Skew)
        {
            var changes = _transforms.Where(t => t.Node.Transform != t.Before).Select(t => (t.Node, t.Before, After: t.Node.Transform)).ToArray();
            if (changes.Length > 0)
            {
                var pivotBefore = _pivotBefore;
                var pivotAfter = CustomPivot;
                Workspace.ExecuteCommand(new DelegateDocumentCommand("Transform selection",
                    () =>
                    {
                        foreach (var change in changes) change.Node.Transform = change.After;
                        CustomPivot = pivotAfter;
                    },
                    () =>
                    {
                        foreach (var change in changes) change.Node.Transform = change.Before;
                        CustomPivot = pivotBefore;
                    }));
            }
            else if (!_hasDragged && _toggleModeOnClick)
            {
                ToggleTransformMode();
            }
        }
        else if (_gesture == Gesture.MovePivot && !_hasDragged)
        {
            if (_resetPivotOnClick) CustomPivot = null;
            else if (_toggleModeOnClick) ToggleTransformMode();
        }
        ClearGesture();
    }

    public override void Cancel()
    {
        _parameters.Cancel();
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate or Gesture.Skew)
            foreach (var (node, _, before) in _transforms) node.Transform = before;
        if (_gesture is Gesture.Move or Gesture.MovePivot)
            CustomPivot = _pivotBefore;
        if (_gesture == Gesture.Marquee) Workspace.SelectNodes(_selectionBefore);
        ClearGesture();
    }

    private void ClearGesture()
    {
        _gesture = Gesture.None;
        _toggleModeOnClick = false;
        _resetPivotOnClick = false;
        Marquee = null;
        _transforms.Clear();
        Workspace.RequestRedraw(false);
    }
}
