using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Services;
using Vectorer.Core;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IEditorClipboard Clipboard { get; set; } = new MemoryEditorClipboard();
    private string? _clipboardPayload;
    private List<SceneNode> _clipboardNodes = [];

    [RelayCommand] private void SelectAll()
    {
        if (IsInteracting) return;
        SelectNodes(GetEditableEntries().Where(e => IsInGroup || e.Container == ActiveLayer).Select(e => e.Node));
    }
    [RelayCommand] private async Task CopySelectionAsync() => _ = await CopyNodesAsync();
    private async Task<bool> CopyNodesAsync()
    {
        if (IsBusy || IsInteracting || SelectedNodes.Count == 0) return false;
        IsBusy = true;
        try
        {
            var nodes = GetEditableEntries().Where(e => SelectedNodes.Contains(e.Node)).Select(e =>
            { var clone = SceneNodeCloner.Clone(e.Node); clone.Transform = SKMatrix.Concat(e.Parent, clone.Transform); return clone; }).ToList();
            if (nodes.Count == 0) return false;
            var document = SvgDocument.CreateDefault(CurrentDocument.Width, CurrentDocument.Height);
            document.Layers[0].Children.AddRange(nodes);
            var payload = _svgSerializer.SerializeToString(document);
            await Clipboard.WriteAsync(payload);
            _clipboardPayload = payload; _clipboardNodes = nodes;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        { StatusMessage = $"コピーできませんでした: {ex.Message}"; return false; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private async Task CutSelectionAsync()
    { if (await CopyNodesAsync()) DeleteSelectedObjects(); }
    [RelayCommand] private async Task PasteAsync()
    {
        if (IsBusy || IsInteracting || GetDrawingTarget() is not { } target || !target.World.TryInvert(out var inverse)) return;
        IsBusy = true;
        try
        {
            var payload = await Clipboard.ReadAsync();
            if (string.IsNullOrWhiteSpace(payload)) return;
            List<SceneNode> nodes;
            if (payload == _clipboardPayload) nodes = _clipboardNodes.Select(n => SceneNodeCloner.Clone(n)).ToList();
            else
            {
                var document = await Task.Run(() => _svgSerializer.DeserializeFromString(payload));
                nodes = document.Layers.SelectMany(l => l.Children.Select(n =>
                { var clone = SceneNodeCloner.Clone(n); clone.Transform = SKMatrix.Concat(l.Transform, clone.Transform); return clone; })).ToList();
            }
            if (nodes.Count == 0) return;
            foreach (var node in nodes) node.Transform = SKMatrix.Concat(inverse, node.Transform);
            ExecuteCommand(new DelegateDocumentCommand("Paste nodes",
                () => target.Container.Children.AddRange(nodes), () => { foreach (var n in nodes) target.Container.Children.Remove(n); }));
            SelectNodes(nodes);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { StatusMessage = $"貼り付けできませんでした: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private void DuplicateSelection()
    {
        if (IsInteracting) return;
        var entries = GetEditableEntries().Where(e => SelectedNodes.Contains(e.Node)).ToArray();
        if (entries.Length == 0) return;
        var copies = entries.Select(e =>
        {
            var clone = SceneNodeCloner.Clone(e.Node);
            clone.Transform = CanvasGeometry.ApplyWorldTransform(clone.Transform, e.Parent, SKMatrix.CreateTranslation(10, 10));
            return (e.Container, Node: clone, Index: e.Index + 1);
        }).ToArray();
        ExecuteCommand(new DelegateDocumentCommand("Duplicate selection", () =>
        { foreach (var group in copies.GroupBy(c => c.Container)) foreach (var c in group.OrderByDescending(c => c.Index)) c.Container.Children.Insert(c.Index, c.Node); },
            () => { foreach (var c in copies) c.Container.Children.Remove(c.Node); }));
        SelectNodes(copies.Select(c => c.Node));
    }
    [RelayCommand] private void ChangeOrder(string mode)
    {
        if (IsInteracting) return;
        var selected = SelectedNodes.ToHashSet();
        var changes = GetEditableEntries().Where(e => selected.Contains(e.Node)).Select(e => e.Container).Distinct().Select(container =>
        {
            var before = container.Children.ToArray(); var after = before.ToList();
            if (mode == "front") after = after.Where(n => !selected.Contains(n)).Concat(after.Where(selected.Contains)).ToList();
            else if (mode == "back") after = after.Where(selected.Contains).Concat(after.Where(n => !selected.Contains(n))).ToList();
            else if (mode == "forward")
            { for (var i = after.Count - 2; i >= 0; i--) if (selected.Contains(after[i]) && !selected.Contains(after[i + 1])) (after[i], after[i + 1]) = (after[i + 1], after[i]); }
            else if (mode == "backward")
            { for (var i = 1; i < after.Count; i++) if (selected.Contains(after[i]) && !selected.Contains(after[i - 1])) (after[i], after[i - 1]) = (after[i - 1], after[i]); }
            return (Container: container, Before: before, After: after.ToArray());
        }).Where(c => !c.Before.SequenceEqual(c.After)).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand("Object order",
            () => { foreach (var c in changes) { c.Container.Children.Clear(); c.Container.Children.AddRange(c.After); } },
            () => { foreach (var c in changes) { c.Container.Children.Clear(); c.Container.Children.AddRange(c.Before); } }));
    }
    [RelayCommand] private void AlignSelection(string mode)
    {
        if (IsInteracting) return;
        var entries = GetEditableNodes().Where(e => SelectedNodes.Contains(e.Node)).Select(e =>
        { using var path = CanvasGeometry.BuildWorldPath(e.Node, e.Parent); return (e.Node, e.Parent, Bounds: path.TightBounds); }).ToArray();
        if (entries.Length < 2) return;
        var bounds = entries.Select(e => e.Bounds).Aggregate(SKRect.Union);
        var translations = new Dictionary<SceneNode, SKPoint>();
        if (mode is "distribute-x" or "distribute-y")
        {
            if (entries.Length < 3) return;
            var horizontal = mode == "distribute-x";
            var sorted = entries.OrderBy(e => horizontal ? e.Bounds.Left : e.Bounds.Top).ToArray();
            var total = sorted.Sum(e => horizontal ? e.Bounds.Width : e.Bounds.Height);
            var extent = horizontal ? sorted[^1].Bounds.Right - sorted[0].Bounds.Left : sorted[^1].Bounds.Bottom - sorted[0].Bounds.Top;
            var gap = (extent - total) / (sorted.Length - 1);
            var position = horizontal ? sorted[0].Bounds.Left : sorted[0].Bounds.Top;
            foreach (var e in sorted)
            { translations[e.Node] = horizontal ? new(position - e.Bounds.Left, 0) : new(0, position - e.Bounds.Top); position += (horizontal ? e.Bounds.Width : e.Bounds.Height) + gap; }
        }
        else foreach (var e in entries) translations[e.Node] = mode switch
        {
            "left" => new(bounds.Left - e.Bounds.Left, 0), "center-x" => new(bounds.MidX - e.Bounds.MidX, 0), "right" => new(bounds.Right - e.Bounds.Right, 0),
            "top" => new(0, bounds.Top - e.Bounds.Top), "center-y" => new(0, bounds.MidY - e.Bounds.MidY), "bottom" => new(0, bounds.Bottom - e.Bounds.Bottom), _ => new(0, 0)
        };
        var changes = entries.Select(e => (e.Node, Before: e.Node.Transform,
            After: CanvasGeometry.ApplyWorldTransform(e.Node.Transform, e.Parent, SKMatrix.CreateTranslation(translations[e.Node].X, translations[e.Node].Y))))
            .Where(c => c.Before != c.After).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand("Align/distribute",
            () => { foreach (var c in changes) c.Node.Transform = c.After; }, () => { foreach (var c in changes) c.Node.Transform = c.Before; }));
    }
}
