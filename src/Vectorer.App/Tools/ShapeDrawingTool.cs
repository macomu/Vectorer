using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Tools;

public sealed class ShapeDrawingTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private SKPoint _start;
    private ContainerNode? _container;
    private bool _drawing;
    private readonly List<SKPoint> _points = [];
    public override bool IsInteracting => _drawing;

    public override void Begin(SKPoint point, bool shift)
    {
        Cancel();
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
        PreviewShape.Style = Workspace.DefaultCreationStyle.Clone();
        if (Workspace.ActiveTool == DrawingTool.Freehand && !Workspace.CloseFreehand) PreviewShape.Style.Fill = null;
        if (PreviewParent.TryInvert(out var inverse)) PreviewShape.Transform = inverse;
        Workspace.RequestRedraw();
    }

    public override void End()
    {
        if (PreviewShape is { } shape && _container is { } container)
        {
            using var path = shape.BuildLocalPath();
            if (Workspace.ActiveTool == DrawingTool.Freehand ? MathF.Max(path.Bounds.Width, path.Bounds.Height) > 1
                : path.Bounds.Width > 1 && path.Bounds.Height > 1)
            {
                Workspace.ExecuteCommand(new AddNodeCommand(container, shape));
                Workspace.SelectNodes([shape]);
            }
        }
        Cancel();
    }

    public override void Cancel()
    {
        _drawing = false;
        PreviewShape = null;
        Workspace.RequestRedraw();
    }
}
