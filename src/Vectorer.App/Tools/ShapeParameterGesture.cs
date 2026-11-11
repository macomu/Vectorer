using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Tools;

public enum ShapeHandleKind { CornerRadius, ArcStart, ArcEnd, OuterRadius, InnerRadius }
public sealed record ShapeParameterHandle(ShapeHandleKind Kind, SKPoint Position);

/// <summary>Parametric shape handles are separate from bounding-box transforms.</summary>
public sealed class ShapeParameterGesture(MainWindowViewModel workspace)
{
    private ShapeNode? _shape;
    private ContainerNode? _container;
    private int _index;
    private SKMatrix _inverse;
    private ShapeHandleKind _kind;
    private Action? _before;
    private bool _changed;
    public bool IsActive => _before is not null;
    public static IReadOnlyList<ShapeParameterHandle> GetHandles(MainWindowViewModel vm)
    {
        if (vm.SelectedNodes.Count != 1) return [];
        var entry = vm.GetEditableNodes().FirstOrDefault(e => e.Node == vm.SelectedNodes[0]);
        if (entry.Node is not ShapeNode shape) return [];
        var world = SKMatrix.Concat(entry.Parent, shape.Transform);
        var pad = (float)(12 / vm.Zoom);
        SKPoint Point(SKPoint center, float rx, float ry, float angle) => world.MapPoint(new SKPoint(center.X + (rx + pad) * MathF.Cos(angle * MathF.PI / 180), center.Y + (ry + pad) * MathF.Sin(angle * MathF.PI / 180)));
        return shape switch
        {
            RectangleShapeNode rect => [new(ShapeHandleKind.CornerRadius, world.MapPoint(new SKPoint(rect.Rect.Right - MathF.Max(pad, rect.CornerRadiusX), rect.Rect.Top + pad)))],
            EllipseShapeNode e => [new(ShapeHandleKind.ArcStart, Point(e.Center, e.RadiusX, e.RadiusY, 0)), new(ShapeHandleKind.ArcEnd, Point(e.Center, e.RadiusX, e.RadiusY, -90))],
            ArcShapeNode a => [new(ShapeHandleKind.ArcStart, Point(a.Center, a.RadiusX, a.RadiusY, a.StartAngleDegrees)), new(ShapeHandleKind.ArcEnd, Point(a.Center, a.RadiusX, a.RadiusY, a.StartAngleDegrees + a.SweepAngleDegrees))],
            PolygonShapeNode p when p.PolygonKind != PolygonKind.Freeform => p.PolygonKind == PolygonKind.ConcaveStar
                ? [new(ShapeHandleKind.OuterRadius, world.MapPoint(p.Points[0])), new(ShapeHandleKind.InnerRadius, world.MapPoint(p.Points[1]))]
                : [new(ShapeHandleKind.OuterRadius, world.MapPoint(p.Points[0]))],
            _ => []
        };
    }
    public bool Begin(SKPoint point)
    {
        var handle = GetHandles(workspace).FirstOrDefault(h => MainWindowViewModel.Distance(h.Position, point) < 7 / workspace.Zoom);
        if (handle is null) return false;
        var entry = workspace.GetEditableEntries().First(e => e.Node == workspace.SelectedNodes[0]);
        _shape = (ShapeNode)entry.Node; _container = entry.Container; _index = entry.Index; _kind = handle.Kind;
        if (!SKMatrix.Concat(entry.Parent, _shape.Transform).TryInvert(out _inverse)) return false;
        _before = Capture(); _changed = false;
        return true;
    }
    public void Move(SKPoint point)
    {
        if (!IsActive || _shape is null) return;
        var local = _inverse.MapPoint(point);
        if (_shape is EllipseShapeNode ellipse)
        {
            var arc = new ArcShapeNode { Id = ellipse.Id, Name = ellipse.Name, Center = ellipse.Center,
                RadiusX = ellipse.RadiusX, RadiusY = ellipse.RadiusY, Style = ellipse.Style.Clone(), Transform = ellipse.Transform,
                Opacity = ellipse.Opacity, BlendMode = ellipse.BlendMode, Filter = ellipse.Filter, ShaderEffect = ellipse.ShaderEffect,
                StartAngleDegrees = 0, SweepAngleDegrees = 270 };
            _container!.Children[_index] = arc; _shape = arc; workspace.SelectNodes([arc]);
        }
        switch (_shape)
        {
            case RectangleShapeNode rect:
                rect.CornerRadiusX = rect.CornerRadiusY = Math.Clamp(rect.Rect.Right - local.X, 0, MathF.Min(rect.Rect.Width, rect.Rect.Height) / 2);
                break;
            case ArcShapeNode arc:
                var angle = MathF.Atan2((local.Y - arc.Center.Y) / MathF.Max(arc.RadiusY, 0.001f), (local.X - arc.Center.X) / MathF.Max(arc.RadiusX, 0.001f)) * 180 / MathF.PI;
                if (_kind == ShapeHandleKind.ArcStart)
                { var end = arc.StartAngleDegrees + arc.SweepAngleDegrees; arc.StartAngleDegrees = angle; arc.SweepAngleDegrees = (end - angle + 720) % 360; }
                else arc.SweepAngleDegrees = (angle - arc.StartAngleDegrees + 720) % 360;
                break;
            case PolygonShapeNode poly:
                var vector = local - poly.Center;
                if (_kind == ShapeHandleKind.InnerRadius) poly.InnerRadius = Math.Min(poly.OuterRadius, PathAnchor.Length(vector));
                else { var ratio = poly.OuterRadius > 0 ? poly.InnerRadius / poly.OuterRadius : 0.45f; poly.OuterRadius = PathAnchor.Length(vector); poly.InnerRadius = poly.OuterRadius * ratio; poly.StartAngleDegrees = MathF.Atan2(vector.Y, vector.X) * 180 / MathF.PI; }
                poly.RegeneratePoints();
                break;
        }
        _changed = true; workspace.RequestRedraw();
    }
    public void End()
    {
        if (_before is not { } before) return;
        var after = Capture(); _before = null;
        if (_changed) workspace.ExecuteCommand(new DelegateDocumentCommand("Shape parameters", after, before));
    }
    public void Cancel()
    {
        var restore = _before; _before = null; restore?.Invoke();
        if (restore is not null && _container is not null) workspace.SelectNodes([_container.Children[_index]]);
        workspace.RequestRedraw();
    }
    private Action Capture()
    {
        var shape = _shape!; var container = _container!; var index = _index;
        Action properties = shape switch
        {
            RectangleShapeNode r => CaptureRect(r), ArcShapeNode a => CaptureArc(a), PolygonShapeNode p => CapturePolygon(p), _ => () => { }
        };
        return () => { properties(); container.Children[index] = shape; };
    }
    private static Action CaptureRect(RectangleShapeNode r) { var x = r.CornerRadiusX; var y = r.CornerRadiusY; return () => { r.CornerRadiusX = x; r.CornerRadiusY = y; }; }
    private static Action CaptureArc(ArcShapeNode a) { var start = a.StartAngleDegrees; var sweep = a.SweepAngleDegrees; return () => { a.StartAngleDegrees = start; a.SweepAngleDegrees = sweep; }; }
    private static Action CapturePolygon(PolygonShapeNode p) { var outer = p.OuterRadius; var inner = p.InnerRadius; var angle = p.StartAngleDegrees; return () => { p.OuterRadius = outer; p.InnerRadius = inner; p.StartAngleDegrees = angle; p.RegeneratePoints(); }; }
}
