using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Tools;

public sealed class ShapeDrawingTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private readonly ShapeResizeGesture _resize = new(workspace);
    private readonly ShapeParameterGesture _parameters = new(workspace);
    private SKPoint _press;
    private SKPoint _start;
    private ContainerNode? _container;
    private bool _drawing;
    private readonly List<SKPoint> _points = [];
    public override bool IsInteracting => _drawing || _resize.IsActive || _parameters.IsActive;

    public override void Begin(SKPoint point, bool shift) => Begin(point, shift, false);
    public void Begin(SKPoint point, bool shift, bool forceNew)
    {
        Cancel();
        _press = point;
        if (!forceNew)
        {
            if (Workspace.SelectedNodes.Count == 1 && ShapeResizeGesture.Matches(Workspace.ActiveTool, Workspace.SelectedNodes[0]))
            {
                if (_parameters.Begin(point) || _resize.Begin(point)) return;
            }
            var hit = Workspace.GetEditableNodes().Reverse().FirstOrDefault(e =>
                CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Workspace.Zoom))).Node;
            if (ShapeResizeGesture.Matches(Workspace.ActiveTool, hit))
            {
                Workspace.SelectNodes([hit!]);
                return;
            }
        }
        var target = Workspace.GetDrawingTarget();
        if (target is null) return;
        (_container, PreviewParent) = target.Value;
        _start = Workspace.SnapPoint(point);
        _points.Clear();
        _points.Add(_start);
        _drawing = true;
    }

    public override void Move(SKPoint point, bool shift, bool alt)
    {
        if (_resize.IsActive) { _resize.Move(point, shift, alt); return; }
        if (_parameters.IsActive)
        {
            if (MainWindowViewModel.Distance(point, _press) > 2 / Workspace.Zoom) _parameters.Move(point);
            return;
        }
        if (!_drawing) return;
        point = Workspace.SnapPoint(point);
        if (Workspace.ActiveTool == DrawingTool.Freehand)
        {
            if (MainWindowViewModel.Distance(_points[^1], point) > 1 / Workspace.Zoom) _points.Add(point);
            PreviewShape = new PathShapeNode(EditablePath.Builder(_points, (float)Workspace.Smoothing, Workspace.CloseFreehand).Build());
        }
        else PreviewShape = CanvasGeometry.CreateShape(Workspace.ActiveTool, _start, point, shift, alt,
            (float)Workspace.CornerRadius, (int)Workspace.PolygonSides, (float)Workspace.InnerRatio,
            (float)Workspace.ArcStart, (float)Workspace.ArcSweep, Workspace.ArcMode);
        if (PreviewShape is RectangleShapeNode rectangle) rectangle.CornerRadiusY = (float)Workspace.CornerRadiusY;
        PreviewShape.Style = Workspace.DefaultCreationStyle.Clone();
        if (Workspace.ActiveTool == DrawingTool.Freehand && !Workspace.CloseFreehand) PreviewShape.Style.Fill = null;
        if (PreviewParent.TryInvert(out var inverse)) PreviewShape.Transform = inverse;
        Workspace.RequestRedraw(false);
    }

    public override void End()
    {
        if (_resize.IsActive) { _resize.End(); return; }
        if (_parameters.IsActive) { _parameters.End(); return; }
        if (PreviewShape is { } shape && _container is { } container)
        {
            using var path = shape.BuildLocalPath();
            if (Workspace.ActiveTool == DrawingTool.Freehand ? MathF.Max(path.Bounds.Width, path.Bounds.Height) > 1
                : path.Bounds.Width > 1 && path.Bounds.Height > 1)
            {
                shape.Name = Workspace.Naming.ShapeName(Workspace.ActiveTool, shape);
                Workspace.ExecuteCommand(new AddNodeCommand(container, shape));
                Workspace.SelectNodes([shape]);
            }
        }
        Cancel();
    }

    public override void Cancel()
    {
        _resize.Cancel(); _parameters.Cancel();
        _drawing = false;
        PreviewShape = null;
        Workspace.RequestRedraw(false);
    }
}
