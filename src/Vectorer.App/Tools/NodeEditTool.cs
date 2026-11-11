using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Tools;

public sealed class NodeEditTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    private ShapeNode? _target;
    private string _loadedData = "";
    private string _loadedKinds = "";
    private bool _editMoved;
    public EditablePath Model { get; private set; } = new();
    public HashSet<PathAnchor> SelectedAnchors { get; } = [];
    public SKMatrix World { get; private set; } = SKMatrix.CreateIdentity();
    private SKMatrix _inverse;
    private Action? _restore;
    private string _beforeData = "";
    private SKPoint _start;
    private SKPoint _worldStart;
    private PathAnchor? _handle;
    private bool _incoming;
    private bool _dragging;
    private bool _marquee;
    private HashSet<PathAnchor> _marqueeBefore = [];
    private (PathContour Contour, int Segment)? _segment;
    private Dictionary<PathAnchor, PathAnchor> _beforeAnchors = [];
    public override bool IsInteracting => _dragging;

    public void Synchronize()
    {
        var entry = Workspace.GetEditableNodes().FirstOrDefault(e => Workspace.SelectedNodes.Count == 1 && e.Node == Workspace.SelectedNodes[0]);
        var target = entry.Node as ShapeNode;
        if (target is not (PathShapeNode or PolygonShapeNode)) target = null;
        using var current = target?.BuildLocalPath();
        var data = current?.ToSvgPathData() ?? "";
        var kinds = target is PathShapeNode p ? p.NodeKinds : "";
        var world = target is null ? SKMatrix.CreateIdentity() : SKMatrix.Concat(entry.Parent, target.Transform);
        if (_target == target && (_dragging || data == _loadedData && kinds == _loadedKinds && World == world)) return;
        _loadedKinds = kinds;
        _loadedData = data;
        _target = target; SelectedAnchors.Clear(); Model = new();
        if (target is null) return;
        World = world;
        if (!World.TryInvert(out _inverse)) { _target = null; return; }
        using var path = target.BuildLocalPath();
        Model = EditablePath.Parse(path.ToSvgPathData());
        Model.FillType = path.FillType;
        var types = kinds.Split(','); var i = 0;
        foreach (var anchor in Model.Contours.SelectMany(c => c.Anchors))
        { if (i < types.Length && Enum.TryParse<PathNodeKind>(types[i], out var kind)) anchor.Kind = kind; i++; }
    }

    public override void Begin(SKPoint point, bool shift)
    {
        Synchronize();
        _worldStart = point; _start = _inverse.MapPoint(point);
        var tolerance = (float)(7 / Workspace.Zoom);
        foreach (var anchor in SelectedAnchors)
        {
            if (anchor.Incoming is { } a && MainWindowViewModel.Distance(World.MapPoint(a), point) < tolerance) { _handle = anchor; _incoming = true; StartEdit(); return; }
            if (anchor.Outgoing is { } b && MainWindowViewModel.Distance(World.MapPoint(b), point) < tolerance) { _handle = anchor; _incoming = false; StartEdit(); return; }
        }
        var nearest = Model.Contours.SelectMany(c => c.Anchors).OrderBy(a => MainWindowViewModel.Distance(World.MapPoint(a.Position), point)).FirstOrDefault();
        if (nearest is not null && MainWindowViewModel.Distance(World.MapPoint(nearest.Position), point) < tolerance)
        {
            if (!shift && !SelectedAnchors.Contains(nearest)) SelectedAnchors.Clear();
            if (shift && !SelectedAnchors.Add(nearest)) { SelectedAnchors.Remove(nearest); return; }
            SelectedAnchors.Add(nearest); StartEdit(); return;
        }
        if (_target is not null && Model.NearestSegment(_start) is { } segment
            && MainWindowViewModel.Distance(World.MapPoint(EditablePath.Evaluate(segment.Contour.Anchors[segment.Segment], segment.Contour.Anchors[(segment.Segment + 1) % segment.Contour.Anchors.Count], segment.T)), point) < tolerance)
        {
            if (_target is PolygonShapeNode) return;
            _segment = (segment.Contour, segment.Segment); StartEdit(); return;
        }
        var hit = Workspace.GetEditableNodes().Reverse().FirstOrDefault(e => CanvasGeometry.HitTest(e.Node, e.Parent, point, tolerance)).Node;
        if (hit is PathShapeNode or PolygonShapeNode)
        { Workspace.SelectNodes([hit]); _target = null; Synchronize(); Workspace.RequestRedraw(false); return; }
        if (_target is not null)
        {
            if (!shift) SelectedAnchors.Clear();
            _marqueeBefore = SelectedAnchors.ToHashSet();
            _marquee = true; _dragging = true;
        }
    }
    private void StartEdit()
    {
        _restore = Capture(); _beforeData = Model.ToSvgPathData();
        _beforeAnchors = Model.Contours.SelectMany(c => c.Anchors).ToDictionary(a => a, a => a.Clone());
        _dragging = true; _editMoved = false;
    }
    public override void Move(SKPoint point, bool shift, bool alt)
    {
        if (!_dragging) return;
        if (_marquee)
        {
            Marquee = CanvasGeometry.DragBounds(_worldStart, point, false, false);
            SelectedAnchors.Clear(); SelectedAnchors.UnionWith(_marqueeBefore);
            foreach (var anchor in Model.Contours.SelectMany(c => c.Anchors))
                if (Marquee.Value.Contains(World.MapPoint(anchor.Position))) SelectedAnchors.Add(anchor);
            Workspace.RequestRedraw(false); return;
        }
        if (!_editMoved && MainWindowViewModel.Distance(_worldStart, point) < 2 / Workspace.Zoom) return;
        _editMoved = true;
        var local = _inverse.MapPoint(Workspace.SnapPoint(point, true));
        if (_handle is not null) _handle.MoveHandle(_incoming, local);
        else if (_segment is { } segment)
        {
            var a = segment.Contour.Anchors[segment.Segment]; var b = segment.Contour.Anchors[(segment.Segment + 1) % segment.Contour.Anchors.Count];
            var d = local - _start;
            a.Outgoing = (_beforeAnchors[a].Outgoing ?? EditablePath.Lerp(a.Position, b.Position, 1f / 3)) + d;
            b.Incoming = (_beforeAnchors[b].Incoming ?? EditablePath.Lerp(a.Position, b.Position, 2f / 3)) + d;
        }
        else foreach (var anchor in SelectedAnchors)
        {
            var before = _beforeAnchors[anchor];
            anchor.Position = before.Position; anchor.Incoming = before.Incoming; anchor.Outgoing = before.Outgoing;
            anchor.MoveTo(before.Position + (local - _start));
        }
        Apply(); _loadedData = Model.ToSvgPathData(); Workspace.RequestRedraw(false);
    }
    public override void End()
    {
        _dragging = false;
        if (!_marquee && _restore is not null && _beforeData != Model.ToSvgPathData())
        {
            var after = Capture(); var before = _restore;
            Workspace.ExecuteCommand(new DelegateDocumentCommand("Edit path nodes", after, before));
        }
        ResetDrag();
    }
    public override void Cancel()
    {
        if (_dragging && !_marquee) _restore?.Invoke();
        ResetDrag(); _target = null; Synchronize();
    }
    public void ClearNodeSelection() { SelectedAnchors.Clear(); Workspace.RequestRedraw(false); }
    private void ResetDrag() { _dragging = false; _marquee = false; _restore = null; _handle = null; _segment = null; Marquee = null; Workspace.RequestRedraw(false); }

    private Action Capture()
    {
        if (_target is PathShapeNode path)
        { var data = path.SvgPathData; var kinds = path.NodeKinds; return () => { path.SvgPathData = data; path.NodeKinds = kinds; }; }
        if (_target is PolygonShapeNode poly)
        { var points = poly.Points.ToList(); var kind = poly.PolygonKind; var closed = poly.IsClosed; return () => { poly.Points = points.ToList(); poly.PolygonKind = kind; poly.IsClosed = closed; }; }
        return () => { };
    }
    private void Apply()
    {
        if (_target is PathShapeNode path)
        { path.SvgPathData = Model.ToSvgPathData(); path.NodeKinds = string.Join(",", Model.Contours.SelectMany(c => c.Anchors).Select(a => a.Kind)); _loadedKinds = path.NodeKinds; }
        else if (_target is PolygonShapeNode poly) { poly.Points = Model.Contours.SelectMany(c => c.Anchors).Select(a => a.Position).ToList(); poly.IsClosed = Model.Contours.FirstOrDefault()?.IsClosed ?? false; poly.ConvertToFreeform(); }
    }
    private void Edit(Action edit, string description)
    {
        Synchronize(); if (_target is null || _dragging) return;
        var before = Capture();
        var signature = Model.ToSvgPathData() + string.Join(",", Model.Contours.SelectMany(c => c.Anchors).Select(a => a.Kind));
        edit();
        if (signature == Model.ToSvgPathData() + string.Join(",", Model.Contours.SelectMany(c => c.Anchors).Select(a => a.Kind))) return;
        Apply(); var after = Capture();
        Workspace.ExecuteCommand(new DelegateDocumentCommand(description, after, before));
        _loadedData = Model.ToSvgPathData();
        Workspace.RequestRedraw(false);
    }
    public void SetCoordinates(float? x, float? y) => Edit(() =>
    {
        foreach (var anchor in SelectedAnchors)
            anchor.MoveTo(new SKPoint(x ?? anchor.Position.X, y ?? anchor.Position.Y));
    }, "Node coordinates");

    public void DeleteNodes()
    {
        if (SelectedAnchors.Count == 0) return;
        if (Model.Contours.Sum(c => c.Anchors.Count) == SelectedAnchors.Count)
        { Workspace.DeleteSelectedObjects(); _target = null; Synchronize(); return; }
        Edit(() => { foreach (var c in Model.Contours) c.Anchors.RemoveAll(a => SelectedAnchors.Contains(a)); SelectedAnchors.Clear(); }, "Delete path nodes");

    }
    public void InsertAt(SKPoint point)
    {
        Synchronize(); if (_target is null) return;
        var local = _inverse.MapPoint(point);
        if (Model.NearestSegment(local) is not { } nearest) return;
        if (MainWindowViewModel.Distance(World.MapPoint(EditablePath.Evaluate(nearest.Contour.Anchors[nearest.Segment], nearest.Contour.Anchors[(nearest.Segment + 1) % nearest.Contour.Anchors.Count], nearest.T)), point) > 10 / Workspace.Zoom) return;
        Edit(() => { var anchor = EditablePath.SplitSegment(nearest.Contour, nearest.Segment, nearest.T); SelectedAnchors.Clear(); SelectedAnchors.Add(anchor); }, "Insert path node");
    }
    public void AddMidpoint() => Edit(() =>
    {
        foreach (var c in Model.Contours)
        { var i = c.Anchors.FindIndex(a => SelectedAnchors.Contains(a)); if (i >= 0 && (c.IsClosed || i < c.Anchors.Count - 1)) { var added = EditablePath.SplitSegment(c, i); SelectedAnchors.Clear(); SelectedAnchors.Add(added); break; } }
    }, "Insert midpoint");
    public void SetKind(PathNodeKind kind) => Edit(() =>
    {
        foreach (var a in SelectedAnchors)
        {
            a.Kind = kind;
            if (kind != PathNodeKind.Corner)
            {
                var handle = a.Outgoing ?? new SKPoint(a.Position.X + 30, a.Position.Y);
                a.MoveHandle(false, handle);
            }
        }
    }, "Node handle type");
    public void SetCurved(bool curved) => Edit(() =>
    {
        foreach (var c in Model.Contours)
            for (var i = 0; i < (c.IsClosed ? c.Anchors.Count : c.Anchors.Count - 1); i++)
                if (SelectedAnchors.Contains(c.Anchors[i]))
                { var a = c.Anchors[i]; var b = c.Anchors[(i + 1) % c.Anchors.Count]; a.Outgoing = curved ? EditablePath.Lerp(a.Position, b.Position, 1f / 3) : null; b.Incoming = curved ? EditablePath.Lerp(a.Position, b.Position, 2f / 3) : null; }
    }, "Segment type");
    public void ToggleClosed() => Edit(() => { foreach (var c in Model.Contours) c.IsClosed = !c.IsClosed; }, "Open/close path");
    public void CutSelected() => Edit(() =>
    {
        foreach (var c in Model.Contours.ToArray()) { var i = c.Anchors.FindIndex(a => SelectedAnchors.Contains(a)); if (i >= 0) { Model.Cut(c, i); SelectedAnchors.Clear(); break; } }
    }, "Cut path");
    public void JoinSelected() => Edit(() =>
    {
        var endpoints = Model.Contours.Where(c => !c.IsClosed && c.Anchors.Count > 0)
            .SelectMany(c => new[] { (Contour: c, Anchor: c.Anchors[0]), (Contour: c, Anchor: c.Anchors[^1]) })
            .Where(e => SelectedAnchors.Contains(e.Anchor)).Distinct().ToArray();
        if (endpoints.Length != 2) return;
        var a = endpoints[0]; var b = endpoints[1];
        if (a.Contour == b.Contour) { a.Contour.IsClosed = true; return; }
        static void Reverse(PathContour c) { c.Anchors.Reverse(); foreach (var n in c.Anchors) (n.Incoming, n.Outgoing) = (n.Outgoing, n.Incoming); }
        if (a.Anchor == a.Contour.Anchors[0]) Reverse(a.Contour);
        if (b.Anchor == b.Contour.Anchors[^1]) Reverse(b.Contour);
        a.Contour.Anchors.AddRange(b.Contour.Anchors); Model.Contours.Remove(b.Contour);
    }, "Join path endpoints");
    public void Simplify(float tolerance) => Edit(() =>
    {
        BezierPathFitter.Simplify(Model, tolerance);
        SelectedAnchors.Clear();
    }, "Simplify path");
}
