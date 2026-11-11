using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ShapeStyle DefaultCreationStyle { get; } = new()
    {
        Fill = new SolidColorFill(new SKColor(56, 141, 255)),
        Stroke = new StrokeStyle(new SKColor(226, 232, 240))
    };
    private readonly List<(GroupNode Group, SKMatrix Parent)> _scope = [];
    public GroupNode? EditingGroup => _scope.Count > 0 ? _scope[^1].Group : null;
    public SKMatrix ScopeParent => _scope.Count > 0 ? _scope[^1].Parent : SKMatrix.CreateIdentity();
    public string ScopePath => _scope.Count == 0 ? "ドキュメント" : string.Join(" > ", _scope.Select(s => s.Group.Name));
    public bool IsInGroup => _scope.Count > 0;

    public IEnumerable<(SceneNode Node, SKMatrix Parent)> GetEditableNodes()
    {
        ValidateScope();
        if (EditingGroup is { } group)
        {
            if (CurrentDocument.FindNodeById(group.Id) != group || !group.IsVisible || group.IsLocked) yield break;
            var parent = SKMatrix.Concat(ScopeParent, group.Transform);
            if (!parent.TryInvert(out _)) yield break;
            foreach (var child in group.Children.Where(n => n.IsVisible && !n.IsLocked && n.Opacity > 0)) yield return (child, parent);
        }
        else foreach (var entry in CanvasGeometry.EditableNodes(CurrentDocument)) yield return entry;
    }
    public IEnumerable<(ContainerNode Container, SceneNode Node, int Index, SKMatrix Parent)> GetEditableEntries()
    {
        foreach (var (node, parent) in GetEditableNodes())
        {
            ContainerNode? container = EditingGroup ?? (ContainerNode?)CurrentDocument.Layers.FirstOrDefault(l => l.Children.Contains(node));
            if (container is not null) yield return (container, node, container.Children.IndexOf(node), parent);
        }
    }
    public (ContainerNode Container, SKMatrix World)? GetDrawingTarget()
    {
        ValidateScope();
        ContainerNode container = EditingGroup ?? (ContainerNode)EnsureActiveLayer();
        var world = EditingGroup is null ? container.Transform : SKMatrix.Concat(ScopeParent, container.Transform);
        if (container.IsLocked || !container.IsVisible || !world.TryInvert(out _))
        { StatusMessage = "描画先が非表示・ロック中、または変換できません。"; return null; }
        return (container, world);
    }
    private void ValidateScope()
    {
        if (_scope.Count == 0) return;
        ContainerNode? container = CurrentDocument.Layers.FirstOrDefault(l => l.Children.Contains(_scope[0].Group) && !l.IsLocked && l.IsVisible);
        var parent = container?.Transform ?? SKMatrix.CreateIdentity();
        for (var i = 0; i < _scope.Count; i++)
        {
            var group = _scope[i].Group;
            if (container is null || !container.Children.Contains(group) || group.IsLocked || !group.IsVisible)
            { _scope.RemoveRange(i, _scope.Count - i); return; }
            _scope[i] = (group, parent);
            parent = SKMatrix.Concat(parent, group.Transform); container = group;
        }
    }
    [RelayCommand] private void EnterGroup()
    {
        if (IsInteracting || SelectedNodes.Count != 1 || SelectedNodes[0] is not GroupNode group) return;
        var entry = GetEditableNodes().FirstOrDefault(e => e.Node == group);
        if (entry.Node is null) return;
        _scope.Add((group, entry.Parent)); SelectNodes([]); NotifyScope();
    }
    [RelayCommand] private void ExitGroup()
    {
        CancelInteraction();
        if (_scope.Count == 0) return;
        _scope.RemoveAt(_scope.Count - 1); SelectNodes([]); NotifyScope();
    }
    private void NotifyScope()
    {
        OnPropertyChanged(nameof(ScopePath)); OnPropertyChanged(nameof(IsInGroup)); RequestRedraw();
    }
    public void HandleEscape()
    {
        if (IsInteracting) { CancelInteraction(); return; }
        if (CanvasTool is Tools.NodeEditTool tool && tool.SelectedAnchors.Count > 0) { tool.ClearNodeSelection(); return; }
        if (SelectedNodes.Count > 0) SelectNodes([]); else ExitGroup();
    }
    public void HandleDoubleClick(SKPoint point)
    {
        if (CanvasTool is Tools.BezierPenTool pen) { pen.Finish(); return; }
        if (CanvasTool is Tools.NodeEditTool nodes) { nodes.InsertAt(point); return; }
        if (ActiveTool == DrawingTool.Select)
        {
            var hit = GetEditableNodes().Reverse().FirstOrDefault(e => CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Zoom))).Node;
            if (hit is GroupNode) { SelectNodes([hit]); EnterGroup(); }
            else if (hit is null && IsInGroup) ExitGroup();
        }
    }
    public SKPoint SnapPoint(SKPoint point, bool excludeSelection = false)
    {
        if (!SnapToGrid) return point;
        var candidates = new List<SKPoint> { CanvasGeometry.Snap(point) };
        foreach (var (node, parent) in GetEditableNodes().Where(e => !excludeSelection || !SelectedNodes.Contains(e.Node)))
        {
            using var path = CanvasGeometry.BuildWorldPath(node, parent);
            var bounds = path.TightBounds;
            candidates.AddRange(GetHandles(bounds)); candidates.Add(new(bounds.MidX, bounds.MidY));
            if (node is PolygonShapeNode polygon)
            { var world = SKMatrix.Concat(parent, polygon.Transform); candidates.AddRange(polygon.Points.Select(world.MapPoint)); }
            else if (node is PathShapeNode shape)
            {
                var model = EditablePath.Parse(shape.SvgPathData); var world = SKMatrix.Concat(parent, shape.Transform);
                candidates.AddRange(model.Contours.SelectMany(c => c.Anchors).Select(a => world.MapPoint(a.Position)));
            }
        }
        return SnapGeometry.Nearest(point, candidates, (float)(8 / Zoom)) ?? CanvasGeometry.Snap(point);
    }
    [RelayCommand] private void GroupSelection()
    {
        var entries = GetEditableEntries().Where(e => SelectedNodes.Contains(e.Node)).ToArray();
        if (IsInteracting || entries.Length < 2 || entries.Select(e => e.Container).Distinct().Count() != 1) return;
        var container = entries[0].Container; var group = new GroupNode { Name = "Group" };
        group.Children.AddRange(entries.Select(e => e.Node));
        ExecuteCommand(new DelegateDocumentCommand("Group selection",
            () => { foreach (var e in entries) container.Children.Remove(e.Node); container.Children.Insert(entries[0].Index, group); },
            () => { container.Children.Remove(group); foreach (var e in entries) container.Children.Insert(e.Index, e.Node); }));
        SelectNodes([group]);
    }
    [RelayCommand] private void UngroupSelection()
    {
        var entry = GetEditableEntries().FirstOrDefault(e => SelectedNodes.Count == 1 && e.Node == SelectedNodes[0] && e.Node is GroupNode);
        if (IsInteracting || entry.Node is not GroupNode group) return;
        var children = group.Children.Select(n => (Node: n, Before: n.Transform, After: SKMatrix.Concat(group.Transform, n.Transform))).ToArray();
        ExecuteCommand(new DelegateDocumentCommand("Ungroup selection", () =>
        {
            entry.Container.Children.Remove(group);
            for (var i = 0; i < children.Length; i++) { children[i].Node.Transform = children[i].After; entry.Container.Children.Insert(entry.Index + i, children[i].Node); }
        }, () =>
        {
            foreach (var c in children) { entry.Container.Children.Remove(c.Node); c.Node.Transform = c.Before; }
            entry.Container.Children.Insert(entry.Index, group);
        }));
        SelectNodes(children.Select(c => c.Node));
    }
}
