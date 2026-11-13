using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core;
using Vectorer.Core.Commands;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private decimal _layerAngle = 90;
    public IReadOnlyList<LayerBlendMode> BlendModes { get; } = Enum.GetValues<LayerBlendMode>();
    private bool CanEditLayer => !IsInteracting && !IsBusy && ActiveLayer is not null && CurrentDocument.Layers.Contains(ActiveLayer);
    [RelayCommand] private void NewLayer()
    {
        if (IsInteracting || IsBusy) return;
        var layer = new LayerNode { Name = Naming.Layer(CurrentDocument.Layers.Count + 1) };
        ExecuteCommand(new DelegateDocumentCommand("New layer", () => CurrentDocument.Layers.Add(layer), () => CurrentDocument.Layers.Remove(layer)));
        ActiveLayer = layer;
    }
    [RelayCommand] private void DuplicateLayer()
    {
        if (!CanEditLayer) return;
        var layer = (LayerNode)SceneNodeCloner.Clone(ActiveLayer!); layer.Name = Naming.CopyOf(ActiveLayer!.Name);
        var index = CurrentDocument.Layers.IndexOf(ActiveLayer!) + 1;
        ExecuteCommand(new DelegateDocumentCommand("Duplicate layer", () => CurrentDocument.Layers.Insert(index, layer), () => CurrentDocument.Layers.Remove(layer)));
        ActiveLayer = layer;
    }
    [RelayCommand] private void DeleteLayer()
    {
        if (!CanEditLayer) return;
        var layer = ActiveLayer!; var index = CurrentDocument.Layers.IndexOf(layer);
        var replacement = new LayerNode { Name = Naming.Layer(1) }; var only = CurrentDocument.Layers.Count == 1;
        SelectNodes([]);
        ExecuteCommand(new DelegateDocumentCommand("Delete layer", () => { CurrentDocument.Layers.Remove(layer); if (only) CurrentDocument.Layers.Add(replacement); },
            () => { if (only) CurrentDocument.Layers.Remove(replacement); CurrentDocument.Layers.Insert(index, layer); }));
    }
    [RelayCommand] private void SetLayerBlend(LayerBlendMode mode)
    {
        if (!CanEditLayer) return;
        var layer = ActiveLayer!; var before = layer.BlendMode;
        if (before == mode) return;
        ExecuteCommand(new DelegateDocumentCommand("Layer blend", () => layer.BlendMode = mode, () => layer.BlendMode = before));
    }
    [RelayCommand] private void ToggleLayerVisibility() => ToggleLayerFlag(false);
    [RelayCommand] private void ToggleLayerLock() => ToggleLayerFlag(true);
    private void ToggleLayerFlag(bool locked)
    {
        if (!CanEditLayer) return;
        var layer = ActiveLayer!; var before = locked ? layer.IsLocked : layer.IsVisible;
        void Apply(bool value) { if (locked) layer.IsLocked = value; else layer.IsVisible = value; }
        ExecuteCommand(new DelegateDocumentCommand(locked ? "Layer lock" : "Layer visibility", () => Apply(!before), () => Apply(before)));
        SelectNodes([]);
    }
    [RelayCommand] private void OrderLayer(string mode)
    {
        if (!CanEditLayer) return;
        var layer = ActiveLayer!; var before = CurrentDocument.Layers.IndexOf(layer);
        var after = mode switch { "front" => CurrentDocument.Layers.Count - 1, "back" => 0, "forward" => Math.Min(before + 1, CurrentDocument.Layers.Count - 1), "backward" => Math.Max(0, before - 1), _ => before };
        if (after == before) return;
        void Apply(int index) { CurrentDocument.Layers.Remove(layer); CurrentDocument.Layers.Insert(index, layer); }
        ExecuteCommand(new DelegateDocumentCommand("Layer order", () => Apply(after), () => Apply(before)));
    }
    [RelayCommand] private void RotateLayer(string operation)
    {
        if (!CanEditLayer || ActiveLayer!.IsLocked) return;
        var layer = ActiveLayer!; var before = layer.Transform;
        var cx = CurrentDocument.Width / 2; var cy = CurrentDocument.Height / 2;
        var delta = operation switch
        {
            "90" => SKMatrix.CreateRotationDegrees(90, cx, cy), "-90" => SKMatrix.CreateRotationDegrees(-90, cx, cy),
            "180" => SKMatrix.CreateRotationDegrees(180, cx, cy), "flip-x" => SKMatrix.CreateScale(-1, 1, cx, cy),
            "flip-y" => SKMatrix.CreateScale(1, -1, cx, cy), _ => SKMatrix.CreateRotationDegrees((float)LayerAngle, cx, cy)
        };
        var after = Vectorer.Core.Geometry.CanvasGeometry.ApplyWorldTransform(before, CurrentDocument.GetViewportTransform(), delta);
        ExecuteCommand(new DelegateDocumentCommand("Layer transform", () => layer.Transform = after, () => layer.Transform = before));
    }
    public bool CanMergeLayer => CanEditLayer && ActiveLayer is { IsLocked: false } layer && CurrentDocument.Layers.IndexOf(layer) > 0
        && CurrentDocument.Layers[CurrentDocument.Layers.IndexOf(layer) - 1] is { IsLocked: false } below && below.Transform.TryInvert(out _);
    [RelayCommand(CanExecute = nameof(CanMergeLayer))] private void MergeLayerDown()
    {
        var upper = ActiveLayer!; var index = CurrentDocument.Layers.IndexOf(upper); var lower = CurrentDocument.Layers[index - 1];
        lower.Transform.TryInvert(out var inverse);
        var children = upper.Children.Select(n => (Node: n, Before: n.Transform, After: SKMatrix.Concat(inverse, SKMatrix.Concat(upper.Transform, n.Transform)))).ToArray();
        ExecuteCommand(new DelegateDocumentCommand("Merge layer down", () =>
        {
            foreach (var c in children) { c.Node.Transform = c.After; lower.Children.Add(c.Node); }
            upper.Children.Clear(); CurrentDocument.Layers.Remove(upper);
        }, () =>
        {
            foreach (var c in children) { lower.Children.Remove(c.Node); c.Node.Transform = c.Before; upper.Children.Add(c.Node); }
            CurrentDocument.Layers.Insert(index, upper);
        }));
        ActiveLayer = lower; SelectNodes(children.Select(c => c.Node));
    }
    partial void OnActiveLayerChanged(LayerNode? value)
    {
        MergeLayerDownCommand.NotifyCanExecuteChanged();
        SyncEffectsInspector();
        SyncTreeSelectionFromWorkspace();
    }
    [RelayCommand] private void ExecutePluginMenu(Vectorer.Abstractions.Plugins.IMenuContribution? contribution)
    { if (!IsInteracting && contribution is not null) contribution.Execute(this); }
    [RelayCommand] private void ClearFilters()
    {
        if (IsInteracting || IsBusy) return;
        var nodes = GetEditableNodes().Select(e => e.Node).Where(SelectedNodes.Contains).ToArray();
        if (nodes.Length == 0 && ActiveLayer is { IsLocked: false } layer) nodes = [layer];
        var changes = nodes.Where(n => n.Filter is not null).Select(n => (Node: n, Filter: n.Filter)).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand("Clear filters", () => { foreach (var c in changes) c.Node.Filter = null; },
            () => { foreach (var c in changes) c.Node.Filter = c.Filter; }));
    }
}
