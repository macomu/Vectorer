using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Tools;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private decimal? _nodeX;
    [ObservableProperty] private decimal? _nodeY;
    public bool HasSelectedAnchors => _canvasTool is NodeEditTool tool && tool.SelectedAnchors.Count > 0;
    private void SyncNodeCoordinates()
    {
        if (_canvasTool is NodeEditTool tool)
        {
            var x = tool.SelectedAnchors.Select(a => (decimal)a.Position.X).Distinct().ToArray();
            var y = tool.SelectedAnchors.Select(a => (decimal)a.Position.Y).Distinct().ToArray();
            NodeX = x.Length == 1 ? x[0] : null; NodeY = y.Length == 1 ? y[0] : null;
        }
        OnPropertyChanged(nameof(HasSelectedAnchors));
    }
    [RelayCommand] private void ApplyNodeCoordinates()
    {
        if (_canvasTool is NodeEditTool tool) tool.SetCoordinates(NodeX is { } x ? (float)x : null, NodeY is { } y ? (float)y : null);
    }
    private void JoinSelectedPaths()
    {
        if (IsInteracting) return;
        var entries = GetEditableEntries().Where(e => SelectedNodes.Contains(e.Node)).ToArray();
        if (entries.Length < 2 || entries.Length != SelectedNodes.Count || entries.Any(e => e.Node is not PathShapeNode)
            || entries.Select(e => e.Container).Distinct().Count() != 1)
        { StatusMessage = "同じ階層の開いたパスを2つ以上選択してください。"; return; }
        var result = (PathShapeNode)entries[0].Node;
        foreach (var entry in entries.Skip(1))
        {
            var joined = PathEditingService.Join(result, (PathShapeNode)entry.Node);
            if (joined is null) { StatusMessage = "結合できる開いた端点がありません。"; return; }
            result = joined;
        }
        var container = entries[0].Container;
        ExecuteCommand(new DelegateDocumentCommand("Join paths", () =>
        {
            foreach (var e in entries) container.Children.Remove(e.Node);
            container.Children.Insert(entries[0].Index, result);
        }, () =>
        {
            container.Children.Remove(result);
            foreach (var e in entries) container.Children.Insert(e.Index, e.Node);
        }));
        SelectNodes([result]);
    }
    private void EditSelectedPaths(bool simplify)
    {
        if (IsInteracting) return;
        if (_canvasTool is NodeEditTool tool && SelectedNodes.Count == 1)
        { if (simplify) tool.Simplify((float)SimplifyTolerance); else tool.ToggleClosed(); return; }
        var changes = GetEditableNodes().Select(e => e.Node).OfType<PathShapeNode>().Where(SelectedNodes.Contains).Select(path =>
        {
            var model = EditablePath.Parse(path.SvgPathData); model.FillType = path.FillType;
            if (simplify) BezierPathFitter.Simplify(model, (float)SimplifyTolerance);
            else foreach (var c in model.Contours) c.IsClosed = !c.IsClosed;
            return (Path: path, Before: path.SvgPathData, Kinds: path.NodeKinds, After: model.ToSvgPathData(), AfterKinds: string.Join(",", model.Contours.SelectMany(c => c.Anchors).Select(a => a.Kind)));
        }).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand(simplify ? "Simplify paths" : "Open/close paths",
            () => { foreach (var c in changes) { c.Path.SvgPathData = c.After; c.Path.NodeKinds = c.AfterKinds; } },
            () => { foreach (var c in changes) { c.Path.SvgPathData = c.Before; c.Path.NodeKinds = c.Kinds; } }));
    }
}
