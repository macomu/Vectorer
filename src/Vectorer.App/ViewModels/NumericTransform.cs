using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private decimal _selectionX;
    [ObservableProperty] private decimal _selectionY;
    [ObservableProperty] private decimal _selectionWidth;
    [ObservableProperty] private decimal _selectionHeight;
    [ObservableProperty] private decimal _selectionRotation;
    [ObservableProperty] private bool _lockAspect;
    private bool _adjustingAspect;
    public bool HasSelection => SelectedNodes.Count > 0;
    private void SyncTransformFields()
    {
        if (GetSelectionBounds() is { } b)
        { SelectionX = (decimal)b.Left; SelectionY = (decimal)b.Top; SelectionWidth = (decimal)b.Width; SelectionHeight = (decimal)b.Height; }
        SelectionRotation = 0;
        OnPropertyChanged(nameof(HasSelection));
    }
    partial void OnSelectionWidthChanged(decimal value)
    {
        if (!_syncingEditors && !_adjustingAspect && LockAspect && GetSelectionBounds() is { Width: > 0 } b)
        {
            _adjustingAspect = true;
            try { SelectionHeight = value * (decimal)(b.Height / b.Width); }
            finally { _adjustingAspect = false; }
        }
    }
    partial void OnSelectionHeightChanged(decimal value)
    {
        if (!_syncingEditors && !_adjustingAspect && LockAspect && GetSelectionBounds() is { Height: > 0 } b)
        {
            _adjustingAspect = true;
            try { SelectionWidth = value * (decimal)(b.Width / b.Height); }
            finally { _adjustingAspect = false; }
        }
    }
    [RelayCommand] private void ApplyTransform()
    {
        if (IsInteracting || GetSelectionBounds() is not { Width: > 0, Height: > 0 } b || SelectionWidth <= 0 || SelectionHeight <= 0) return;
        var scale = SKMatrix.CreateScale((float)SelectionWidth / b.Width, (float)SelectionHeight / b.Height, b.Left, b.Top);
        var move = SKMatrix.CreateTranslation((float)SelectionX - b.Left, (float)SelectionY - b.Top);
        var rotation = SKMatrix.CreateRotationDegrees((float)SelectionRotation, (float)(SelectionX + SelectionWidth / 2), (float)(SelectionY + SelectionHeight / 2));
        ApplySelectionMatrix(SKMatrix.Concat(rotation, SKMatrix.Concat(move, scale)), "Numeric transform");
    }
    [RelayCommand] private void FlipHorizontal()
    { if (GetSelectionBounds() is { } b) ApplySelectionMatrix(SKMatrix.CreateScale(-1, 1, b.MidX, b.MidY), "Flip horizontal"); }
    [RelayCommand] private void FlipVertical()
    { if (GetSelectionBounds() is { } b) ApplySelectionMatrix(SKMatrix.CreateScale(1, -1, b.MidX, b.MidY), "Flip vertical"); }
    private void ApplySelectionMatrix(SKMatrix delta, string description)
    {
        if (IsInteracting) return;
        var changes = GetEditableNodes().Where(e => SelectedNodes.Contains(e.Node))
            .Select(e => (e.Node, Before: e.Node.Transform, After: CanvasGeometry.ApplyWorldTransform(e.Node.Transform, e.Parent, delta))).ToArray();
        if (changes.Length == 0) return;
        ExecuteCommand(new DelegateDocumentCommand(description,
            () => { foreach (var c in changes) c.Node.Transform = c.After; },
            () => { foreach (var c in changes) c.Node.Transform = c.Before; }));
    }
}
