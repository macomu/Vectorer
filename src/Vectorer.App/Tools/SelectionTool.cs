using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;
using static Vectorer.App.ViewModels.MainWindowViewModel;

namespace Vectorer.App.Tools;

public sealed class SelectionTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private readonly ShapeParameterGesture _parameters = new(workspace);
    private SKPoint _pressPoint;
    private enum Gesture { None, Move, Marquee, Scale, Rotate }
    private Gesture _gesture;
    private SKPoint _start;
    private SKRect _selectionBounds;
    private SKPoint _anchor;
    private bool _hasDragged;
    private readonly List<(SceneNode Node, SKMatrix Parent, SKMatrix Before)> _transforms = [];
    private List<SceneNode> _selectionBefore = [];
    public override bool IsInteracting => _gesture != Gesture.None || _parameters.IsActive;
    public override void Begin(SKPoint point, bool shift)
    {
        Cancel();
        _start = point;
        _pressPoint = point;
        if (_parameters.Begin(point)) return;
        _hasDragged = false;
        _selectionBefore = Workspace.SelectedNodes.ToList();
        var bounds = Workspace.GetSelectionBounds();
        var tolerance = (float)(7 / Workspace.Zoom);
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
            else if (Distance(new SKPoint(selected.MidX, selected.Top - (float)(24 / Workspace.Zoom)), point) <= tolerance)
            {
                _selectionBounds = selected;
                _gesture = Gesture.Rotate;
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
                else if (!Workspace.SelectedNodes.Contains(hit)) Workspace.SelectNodes([hit]);
                _gesture = Gesture.Move;
            }
        }
        _transforms.Clear();
        _transforms.AddRange(Workspace.GetEditableNodes().Where(e => Workspace.SelectedNodes.Contains(e.Node))
            .Select(e => (e.Node, e.Parent, e.Node.Transform)));
    }

    public override void Move(SKPoint point, bool shift, bool alt)
    {
        if (_parameters.IsActive) { if (Distance(point, _pressPoint) > 2 / Workspace.Zoom) _parameters.Move(point); return; }
        if (_gesture == Gesture.None) return;
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
        {
            if (!_hasDragged && Distance(point, _start) < 2 / Workspace.Zoom) return;
            _hasDragged = true;
        }
        if (Workspace.SnapToGrid) point = Workspace.SnapPoint(point, true);
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
            default:
                SKMatrix delta;
                if (_gesture == Gesture.Move)
                {
                    var origin = Workspace.SnapToGrid ? CanvasGeometry.Snap(_start) : _start;
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
        Workspace.RequestRedraw();
    }

    public override void End()
    {
        if (_parameters.IsActive) { _parameters.End(); return; }
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
        {
            var changes = _transforms.Where(t => t.Node.Transform != t.Before).Select(t => (t.Node, t.Before, After: t.Node.Transform)).ToArray();
            if (changes.Length > 0)
                Workspace.ExecuteCommand(new DelegateDocumentCommand("Transform selection",
                    () => { foreach (var change in changes) change.Node.Transform = change.After; },
                    () => { foreach (var change in changes) change.Node.Transform = change.Before; }));
        }
        ClearGesture();
    }

    public override void Cancel()
    {
        _parameters.Cancel();
        if (_gesture is Gesture.Move or Gesture.Scale or Gesture.Rotate)
            foreach (var (node, _, before) in _transforms) node.Transform = before;
        if (_gesture == Gesture.Marquee) Workspace.SelectNodes(_selectionBefore);
        ClearGesture();
    }
    private void ClearGesture() { _gesture = Gesture.None; Marquee = null; _transforms.Clear(); Workspace.RequestRedraw(); }
}
