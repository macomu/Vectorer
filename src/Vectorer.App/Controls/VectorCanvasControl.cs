using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Controls;

/// <summary>Viewport rendering and input routing; document edits live in the workspace.</summary>
public sealed class VectorCanvasControl : Control
{
    public static readonly StyledProperty<MainWindowViewModel?> WorkspaceProperty =
        AvaloniaProperty.Register<VectorCanvasControl, MainWindowViewModel?>(nameof(Workspace));
    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<VectorCanvasControl, int>(nameof(RenderVersion));
    public MainWindowViewModel? Workspace { get => GetValue(WorkspaceProperty); set => SetValue(WorkspaceProperty, value); }
    public int RenderVersion { get => GetValue(RenderVersionProperty); set => SetValue(RenderVersionProperty, value); }
    private Point _offset = new(32, 32);
    private Point _panStart;
    private Point _offsetBeforePan;
    private bool _panning;
    private bool _space;
    private IPointer? _pointer;

    static VectorCanvasControl() => AffectsRender<VectorCanvasControl>(WorkspaceProperty, RenderVersionProperty);
    public VectorCanvasControl() { Focusable = true; ClipToBounds = true; }
    private SKPoint ToDocument(Point point) => new((float)((point.X - _offset.X) / Workspace!.Zoom), (float)((point.Y - _offset.Y) / Workspace!.Zoom));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Workspace is not { } vm || Bounds.Width <= 1 || Bounds.Height <= 1) return;
        var scaling = VisualRoot?.RenderScaling ?? 1;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling), PixelFormat.Rgba8888, AlphaFormat.Premul);
        using (var fb = bitmap.Lock())
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), fb.Address, fb.RowBytes);
            if (surface is null) return;
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(13, 17, 23));
            canvas.Scale((float)scaling);
            canvas.Translate((float)_offset.X, (float)_offset.Y);
            canvas.Scale((float)vm.Zoom);
            var doc = vm.CurrentDocument;
            using var paper = new SKPaint { Color = new SKColor(30, 36, 45) };
            canvas.DrawRect(0, 0, doc.Width, doc.Height, paper);
            canvas.Save();
            canvas.ClipRect(new SKRect(0, 0, doc.Width, doc.Height));
            vm.DocumentRenderer.RenderDocument(doc, canvas);
            var unit = (float)(1 / vm.Zoom);
            if (vm.ShowGrid)
            {
                using var grid = new SKPaint { Color = new SKColor(148, 163, 184, 60), StrokeWidth = unit };
                // Keep the grid legible and bounded at very low zoom levels.
                var step = vm.Zoom < 0.4 ? 100 : 20;
                var left = Math.Max(0, (int)(-_offset.X / vm.Zoom / step) * step);
                var top = Math.Max(0, (int)(-_offset.Y / vm.Zoom / step) * step);
                var right = Math.Min(doc.Width, (float)((Bounds.Width - _offset.X) / vm.Zoom));
                var bottom = Math.Min(doc.Height, (float)((Bounds.Height - _offset.Y) / vm.Zoom));
                for (var x = left; x <= right; x += step) canvas.DrawLine(x, 0, x, doc.Height, grid);
                for (var y = top; y <= bottom; y += step) canvas.DrawLine(0, y, doc.Width, y, grid);
            }
            if (vm.PreviewShape is { } preview)
            {
                canvas.Save();
                var parent = vm.PreviewParent;
                canvas.Concat(ref parent);
                vm.DocumentRenderer.RenderNodes([preview], canvas);
                canvas.Restore();
            }
            canvas.Restore();
            using var outline = new SKPaint { Color = new SKColor(96, 165, 250), Style = SKPaintStyle.Stroke, StrokeWidth = unit, IsAntialias = true };
            using var handle = new SKPaint { Color = new SKColor(226, 232, 240), IsAntialias = true };
            if (vm.GetSelectionBounds() is { } bounds)
            {
                canvas.DrawRect(bounds, outline);
                foreach (var point in MainWindowViewModel.GetHandles(bounds))
                {
                    var rect = SKRect.Create(point.X - 3 * unit, point.Y - 3 * unit, 6 * unit, 6 * unit);
                    canvas.DrawRect(rect, handle);
                    canvas.DrawRect(rect, outline);
                }
                canvas.DrawLine(bounds.MidX, bounds.Top, bounds.MidX, bounds.Top - 24 * unit, outline);
                canvas.DrawCircle(bounds.MidX, bounds.Top - 24 * unit, 4 * unit, handle);
            }
            if (vm.Marquee is { } marquee)
            {
                using var fill = new SKPaint { Color = new SKColor(59, 130, 246, 35) };
                canvas.DrawRect(marquee, fill);
                canvas.DrawRect(marquee, outline);
            }
        }
        context.DrawImage(bitmap, new Rect(0, 0, width, height), new Rect(Bounds.Size));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Workspace is not { } vm) return;
        var current = e.GetCurrentPoint(this);
        if (!current.Properties.IsLeftButtonPressed && !current.Properties.IsMiddleButtonPressed) return;
        Focus();
        _pointer = e.Pointer;
        _panning = current.Properties.IsMiddleButtonPressed || _space || vm.ActiveTool == DrawingTool.Pan;
        if (_panning) { vm.CancelInteraction(); _panStart = current.Position; _offsetBeforePan = _offset; }
        else vm.BeginInteraction(ToDocument(current.Position), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Workspace is not { } vm) return;
        var point = e.GetPosition(this);
        if (_panning) { _offset = _offsetBeforePan + (point - _panStart); InvalidateVisual(); }
        else vm.UpdateInteraction(ToDocument(point), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pointer != e.Pointer) return;
        if (!_panning && Workspace is { } vm)
        {
            vm.UpdateInteraction(ToDocument(e.GetPosition(this)), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
            vm.EndInteraction();
        }
        _panning = false;
        _pointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        Workspace?.CancelInteraction();
        _panning = false;
        _pointer = null;
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Workspace is not { IsInteracting: false } vm || _panning) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var point = e.GetPosition(this);
            var docPoint = ToDocument(point);
            vm.Zoom = Math.Clamp(vm.Zoom * Math.Pow(1.2, e.Delta.Y), 0.1, 32);
            _offset = new Point(point.X - docPoint.X * vm.Zoom, point.Y - docPoint.Y * vm.Zoom);
        }
        else _offset += new Vector(e.Delta.X * 40, e.Delta.Y * 40);
        InvalidateVisual();
        e.Handled = true;
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Workspace is not { } vm) return;
        if (e.Key == Key.Escape)
        {
            if (_panning) _offset = _offsetBeforePan;
            if (vm.IsInteracting) vm.CancelInteraction(); else vm.SelectNodes([]);
            _panning = false;
            _pointer?.Capture(null);
            _pointer = null;
            InvalidateVisual();
        }
        else if (vm.IsInteracting || _panning) return;
        else if (e.Key == Key.Space) _space = true;
        else if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            switch (e.Key)
            {
                case Key.Z: if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) vm.RedoCommand.Execute(null); else vm.UndoCommand.Execute(null); break;
                case Key.Y: vm.RedoCommand.Execute(null); break;
                case Key.OemPlus: case Key.Add: vm.ZoomInCommand.Execute(null); break;
                case Key.OemMinus: case Key.Subtract: vm.ZoomOutCommand.Execute(null); break;
                case Key.D1: vm.ResetZoomCommand.Execute(null); break;
                case Key.D0:
                    vm.Zoom = Math.Clamp(Math.Min((Bounds.Width - 64) / vm.CurrentDocument.Width, (Bounds.Height - 64) / vm.CurrentDocument.Height), 0.1, 32);
                    _offset = new Point((Bounds.Width - vm.CurrentDocument.Width * vm.Zoom) / 2, (Bounds.Height - vm.CurrentDocument.Height * vm.Zoom) / 2);
                    break;
                case Key.OemQuotes: vm.ShowGrid = !vm.ShowGrid; break;
                case Key.OemSemicolon when e.KeyModifiers.HasFlag(KeyModifiers.Shift): vm.SnapToGrid = !vm.SnapToGrid; break;
                case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Shift): vm.ObjectToPathCommand.Execute(null); break;
                case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Alt): vm.StrokeToPathCommand.Execute(null); break;
                default: return;
            }
        }
        else if (e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            switch (e.Key)
            {
                case Key.V: case Key.S: vm.ChooseTool(DrawingTool.Select); break;
                case Key.R: vm.ChooseTool(DrawingTool.Rectangle); break;
                case Key.E: vm.ChooseTool(DrawingTool.Ellipse); break;
                case Key.Y: vm.ChooseTool(DrawingTool.Polygon); break;
                case Key.P: vm.ChooseTool(DrawingTool.Freehand); break;
                case Key.H: vm.ChooseTool(DrawingTool.Pan); break;
                case Key.Delete: case Key.Back: vm.DeleteSelectionCommand.Execute(null); break;
                default: return;
            }
        }
        else return;
        e.Handled = true;
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.Key == Key.Space) _space = false; }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _space = false;
        if (_pointer is not null) { Workspace?.CancelInteraction(); _panning = false; _pointer.Capture(null); _pointer = null; }
    }
}
