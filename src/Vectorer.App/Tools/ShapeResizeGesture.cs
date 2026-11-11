using Vectorer.Core.Geometry;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;

namespace Vectorer.App.Tools;

/// <summary>Resizes local geometry while keeping the node's transform and paint unchanged.</summary>
public sealed class ShapeResizeGesture(MainWindowViewModel workspace)
{
    private ShapeNode? _shape;
    private SKRect _before;
    private SKMatrix _inverse;
    private int _handle;
    private SKPoint _press;
    private bool _changed;
    public bool IsActive => _shape is not null;

    public static bool Matches(DrawingTool tool, SceneNode? node) => (tool, node) switch
    {
        (DrawingTool.Rectangle, RectangleShapeNode) => true,
        (DrawingTool.Ellipse or DrawingTool.Arc, EllipseShapeNode or ArcShapeNode) => true,
        (DrawingTool.Polygon, PolygonShapeNode { PolygonKind: PolygonKind.RegularConvex }) => true,
        (DrawingTool.Star, PolygonShapeNode { PolygonKind: PolygonKind.ConcaveStar }) => true,
        _ => false
    };

    private static SKRect? Bounds(SceneNode node) => node switch
    {
        RectangleShapeNode r => r.Rect,
        EllipseShapeNode e => SKRect.Create(e.Center.X - e.RadiusX, e.Center.Y - e.RadiusY, 2 * e.RadiusX, 2 * e.RadiusY),
        ArcShapeNode a => SKRect.Create(a.Center.X - a.RadiusX, a.Center.Y - a.RadiusY, 2 * a.RadiusX, 2 * a.RadiusY),
        _ => null
    };

    public static SKPoint[] GetHandles(MainWindowViewModel vm)
    {
        if (vm.SelectedNodes.Count != 1 || !Matches(vm.ActiveTool, vm.SelectedNodes[0])) return [];
        var entry = vm.GetEditableNodes().FirstOrDefault(e => e.Node == vm.SelectedNodes[0]);
        if (entry.Node is null || Bounds(entry.Node) is not { } bounds) return [];
        var world = SKMatrix.Concat(entry.Parent, entry.Node.Transform);
        return MainWindowViewModel.GetHandles(bounds).Select(world.MapPoint).ToArray();
    }

    public bool Begin(SKPoint point)
    {
        var handles = GetHandles(workspace);
        var index = Array.FindIndex(handles, h => MainWindowViewModel.Distance(h, point) <= 7 / workspace.Zoom);
        if (index < 0) return false;
        var entry = workspace.GetEditableNodes().First(e => e.Node == workspace.SelectedNodes[0]);
        if (!SKMatrix.Concat(entry.Parent, entry.Node.Transform).TryInvert(out _inverse)) return false;
        _shape = (ShapeNode)entry.Node; _before = Bounds(_shape)!.Value;
        _handle = index; _press = point; _changed = false;
        return true;
    }

    public void Move(SKPoint point, bool shift, bool alt)
    {
        if (_shape is null || MainWindowViewModel.Distance(point, _press) < 2 / workspace.Zoom) return;
        var p = _inverse.MapPoint(workspace.SnapPoint(point));
        var handles = MainWindowViewModel.GetHandles(_before);
        var anchor = alt ? new SKPoint(_before.MidX, _before.MidY) : handles[(_handle + 4) % 8];
        var x = _handle != 1 && _handle != 5;
        var y = _handle != 3 && _handle != 7;
        var dx = p.X - anchor.X; var dy = p.Y - anchor.Y;
        if (shift && x && y)
        {
            var ratio = _before.Width / _before.Height;
            if (MathF.Abs(dx) > MathF.Abs(dy) * ratio) dy = MathF.CopySign(MathF.Abs(dx) / ratio, dy);
            else dx = MathF.CopySign(MathF.Abs(dy) * ratio, dx);
        }
        var left = x ? MathF.Min(anchor.X, anchor.X + dx) : _before.Left;
        var right = x ? MathF.Max(anchor.X, anchor.X + dx) : _before.Right;
        var top = y ? MathF.Min(anchor.Y, anchor.Y + dy) : _before.Top;
        var bottom = y ? MathF.Max(anchor.Y, anchor.Y + dy) : _before.Bottom;
        if (alt)
        {
            if (x) { left = anchor.X - MathF.Abs(dx); right = anchor.X + MathF.Abs(dx); }
            if (y) { top = anchor.Y - MathF.Abs(dy); bottom = anchor.Y + MathF.Abs(dy); }
        }
        var bounds = new SKRect(left, top, right, bottom);
        if (bounds.Width < 0.01f || bounds.Height < 0.01f) return;
        Apply(_shape, bounds); _changed = bounds != _before;
        workspace.RequestRedraw();
    }

    public void End()
    {
        if (_shape is not { } shape) return;
        var before = _before; var after = Bounds(shape)!.Value; _shape = null;
        if (_changed) workspace.ExecuteCommand(new DelegateDocumentCommand("Resize shape geometry",
            () => Apply(shape, after), () => Apply(shape, before)));
    }

    public void Cancel()
    {
        if (_shape is { } shape) Apply(shape, _before);
        _shape = null; _changed = false; workspace.RequestRedraw();
    }

    private static void Apply(ShapeNode shape, SKRect bounds)
    {
        var center = new SKPoint(bounds.MidX, bounds.MidY);
        switch (shape)
        {
            case RectangleShapeNode r: r.Rect = bounds; break;
            case EllipseShapeNode e: e.Center = center; e.RadiusX = bounds.Width / 2; e.RadiusY = bounds.Height / 2; break;
            case ArcShapeNode a: a.Center = center; a.RadiusX = bounds.Width / 2; a.RadiusY = bounds.Height / 2; break;
        }
    }
}
