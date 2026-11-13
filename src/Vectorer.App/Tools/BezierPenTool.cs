using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Tools;

public sealed class BezierPenTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private readonly EditablePath _model = new();
    private PathContour? _contour;
    private ContainerNode? _container;
    private SKPoint _pressed;
    private bool _closing;
    public override bool IsInteracting => _contour is not null;
    public IReadOnlyList<PathAnchor> Anchors => _contour?.Anchors ?? [];

    public override void Begin(SKPoint point, bool shift)
    {
        if (_contour is null)
        {
            if (Workspace.GetDrawingTarget() is not { } target) return;
            (_container, PreviewParent) = target;
            _contour = new PathContour(); _model.Contours.Add(_contour);
        }
        point = Workspace.SnapPoint(point);
        _pressed = point;
        if (_contour.Anchors.Count > 2 && MainWindowViewModel.Distance(_contour.Anchors[0].Position, point) < 8 / Workspace.Zoom)
        { _closing = true; return; }
        if (_contour.Anchors.Count > 0 && MainWindowViewModel.Distance(_contour.Anchors[^1].Position, point) < 2 / Workspace.Zoom) return;
        _contour.Anchors.Add(new PathAnchor(point));
        Refresh();
    }
    public override void Move(SKPoint point, bool shift, bool alt)
    {
        if (_closing || _contour is null || _contour.Anchors.Count == 0) return;
        if (Workspace.BezierPolyline || MainWindowViewModel.Distance(_pressed, point) < 2 / Workspace.Zoom) return;
        var anchor = _contour.Anchors[^1];
        anchor.Kind = PathNodeKind.Symmetric;
        anchor.Outgoing = Workspace.SnapPoint(point);
        anchor.Incoming = anchor.Position - (anchor.Outgoing.Value - anchor.Position);
        Refresh();
    }
    public override void End() { if (_closing) Finish(true); }
    public void Finish(bool closed = false)
    {
        if (_contour is null || _container is null) return;
        _contour.IsClosed = closed;
        if (_contour.Anchors.Count >= 2)
        {
            Refresh();
            var shape = PreviewShape!;
            shape.Name = Workspace.Naming.PathName();
            Workspace.ExecuteCommand(new AddNodeCommand(_container, shape, "Draw Bezier path"));
            Workspace.SelectNodes([shape]);
        }
        Cancel();
    }
    private void Refresh()
    {
        PreviewShape = new PathShapeNode(_model.ToSvgPathData()) { Style = Workspace.DefaultCreationStyle.Clone() };
        PreviewShape.Style.Fill = _contour is { IsClosed: true } ? PreviewShape.Style.Fill : null;
        if (PreviewParent.TryInvert(out var inverse)) PreviewShape.Transform = inverse;
        Workspace.RequestRedraw(false);
    }
    public override void Cancel() { _contour = null; _model.Contours.Clear(); PreviewShape = null; _closing = false; Workspace.RequestRedraw(false); }
}
