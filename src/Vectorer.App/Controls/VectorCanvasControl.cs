using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Vectorer.App.Tools;
using Vectorer.Abstractions.Documents;
using Avalonia.Threading;

namespace Vectorer.App.Controls;

/// <summary>Viewport rendering and input routing; document edits live in the workspace.</summary>
public sealed class VectorCanvasControl : Decorator
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
    private TextBox? _textEditor;
    private SKPoint _textPosition;
    private float _textSize;
    private readonly object _panGuardToken = new();
    private readonly object _inlineTextGuardToken = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WorkspaceProperty)
        {
            if (change.OldValue is MainWindowViewModel before)
            {
                before.TextEditRequested -= OpenTextEditor;
                before.FinishTextEditingRequested -= CommitInlineText;
                before.FitCanvasRequested -= FitDocument;
                before.LanguageSafety.SetInputGuard(_panGuardToken, false);
                before.LanguageSafety.SetInputGuard(_inlineTextGuardToken, false);
            }
            if (change.NewValue is MainWindowViewModel after) { after.TextEditRequested += OpenTextEditor; after.FinishTextEditingRequested += CommitInlineText; after.FitCanvasRequested += FitDocument; }
        }
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Workspace is { } vm)
        {
            vm.LanguageSafety.SetInputGuard(_panGuardToken, false);
            vm.LanguageSafety.SetInputGuard(_inlineTextGuardToken, false);
        }
    }
    private void SetPanning(bool panning)
    {
        _panning = panning;
        Workspace?.LanguageSafety.SetInputGuard(_panGuardToken, panning);
    }
    private void OpenTextEditor(TextShapeNode text, SKPoint point)
    {
        if (_textEditor is not null) CloseTextEditor(true);
        _textPosition = point; _textSize = text.FontSize;
        var editor = new TextBox { Text = text.Text, AcceptsReturn = true, MinWidth = 240, MinHeight = 70,
            FontFamily = new FontFamily(text.FontFamily), FontSize = Math.Clamp(text.FontSize * Workspace!.Zoom, 10, 120),
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)) };
        _textEditor = editor;
        Workspace?.LanguageSafety.SetInputGuard(_inlineTextGuardToken, true);
        editor.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseTextEditor(false); e.Handled = true; }
            else if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { CloseTextEditor(true); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        editor.LostFocus += (_, _) => { if (_textEditor == editor) CloseTextEditor(true); };
        Child = editor; InvalidateMeasure();
        Dispatcher.UIThread.Post(() => { if (_textEditor == editor) { editor.Focus(); editor.SelectAll(); } });
    }
    private void CloseTextEditor(bool commit)
    {
        if (_textEditor is not { } editor) return;
        _textEditor = null;
        var workspace = Workspace;
        workspace?.CommitText(commit ? editor.Text ?? "" : null);
        Child = null; Focus(); InvalidateVisual();
        if (workspace is not null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_textEditor is null)
                {
                    workspace.LanguageSafety.SetInputGuard(_inlineTextGuardToken, false);
                }
            }, DispatcherPriority.Input);
        }
    }
    private void FitDocument()
    {
        if (Workspace is not { } vm || Bounds.Width < 65 || Bounds.Height < 65) return;
        vm.Zoom = Math.Clamp(Math.Min((Bounds.Width - 64) / vm.CurrentDocument.Width, (Bounds.Height - 64) / vm.CurrentDocument.Height), 0.1, 32);
        _offset = new Point((Bounds.Width - vm.CurrentDocument.Width * vm.Zoom) / 2, (Bounds.Height - vm.CurrentDocument.Height * vm.Zoom) / 2);
        InvalidateVisual();
    }
    private void CommitInlineText() => CloseTextEditor(true);
    protected override Size MeasureOverride(Size availableSize)
    {
        _textEditor?.Measure(new Size(360, 180));
        return new Size(double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 600 : availableSize.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_textEditor is { } editor && Workspace is { } vm)
            editor.Arrange(new Rect(new Point(Math.Clamp(_offset.X + _textPosition.X * vm.Zoom, 0, Math.Max(0, finalSize.Width - 300)),
                Math.Clamp(_offset.Y + (_textPosition.Y - _textSize) * vm.Zoom, 0, Math.Max(0, finalSize.Height - 120))), new Size(300, 120)));
        return finalSize;
    }


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
            if (vm.EditingGroup is { } editingGroup)
            {
                using var dim = new SKPaint { Color = new SKColor(255, 255, 255, 70) };
                canvas.SaveLayer(dim); vm.DocumentRenderer.RenderDocument(doc, canvas); canvas.Restore();
                canvas.Save(); var scopeParent = vm.ScopeParent; canvas.Concat(ref scopeParent);
                vm.DocumentRenderer.RenderNodes([editingGroup], canvas); canvas.Restore();
            }
            else vm.DocumentRenderer.RenderDocument(doc, canvas);
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
            if (!vm.IsShapeEditing && vm.ActiveTool != DrawingTool.Node && vm.GetSelectionBounds() is { } bounds)
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
            if (vm.IsShapeEditing)
            {
                var points = ShapeResizeGesture.GetHandles(vm);
                if (points.Length == 8)
                {
                    for (var i = 0; i < points.Length; i++)
                    {
                        canvas.DrawLine(points[i], points[(i + 1) % points.Length], outline);
                        canvas.DrawRect(SKRect.Create(points[i].X - 3 * unit, points[i].Y - 3 * unit, 6 * unit, 6 * unit), handle);
                    }
                }
            }
            if (vm.ActiveTool == DrawingTool.Select || vm.IsShapeEditing)
            {
                using var parameterPaint = new SKPaint { Color = new SKColor(251, 191, 36), IsAntialias = true };
                foreach (var parameter in ShapeParameterGesture.GetHandles(vm))
                { canvas.DrawCircle(parameter.Position, 4 * unit, parameterPaint); canvas.DrawCircle(parameter.Position, 4 * unit, outline); }
            }
            if (vm.CanvasTool is NodeEditTool nodes)
            {
                nodes.Synchronize();
                foreach (var anchor in nodes.Model.Contours.SelectMany(c => c.Anchors))
                {
                    var position = nodes.World.MapPoint(anchor.Position);
                    if (nodes.SelectedAnchors.Contains(anchor))
                    {
                        foreach (var direction in new[] { anchor.Incoming, anchor.Outgoing })
                            if (direction is { } value)
                            {
                                var end = nodes.World.MapPoint(value); canvas.DrawLine(position, end, outline);
                                canvas.DrawCircle(end, 3 * unit, handle); canvas.DrawCircle(end, 3 * unit, outline);
                            }
                    }
                    using var nodeFill = new SKPaint { Color = nodes.SelectedAnchors.Contains(anchor) ? new SKColor(59, 130, 246) : SKColors.White };
                    var rect = SKRect.Create(position.X - 4 * unit, position.Y - 4 * unit, 8 * unit, 8 * unit);
                    canvas.DrawRect(rect, nodeFill); canvas.DrawRect(rect, outline);
                }
            }
            if (vm.CanvasTool is BezierPenTool pen)
                foreach (var anchor in pen.Anchors)
                {
                    canvas.DrawCircle(anchor.Position, 3 * unit, handle);
                    if (anchor.Outgoing is { } direction) { canvas.DrawLine(anchor.Position, direction, outline); canvas.DrawCircle(direction, 3 * unit, handle); }
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
        if (_textEditor is not null) return;
        Focus();
        if (e.ClickCount == 2 && !_space && !current.Properties.IsMiddleButtonPressed)
        { vm.HandleDoubleClick(ToDocument(current.Position)); e.Handled = true; return; }
        if (vm.ActiveTool == DrawingTool.Text && !current.Properties.IsMiddleButtonPressed && !_space)
        { vm.BeginTextEdit(ToDocument(current.Position)); e.Handled = true; return; }
        _pointer = e.Pointer;
        SetPanning(current.Properties.IsMiddleButtonPressed || _space || vm.ActiveTool == DrawingTool.Pan);
        if (_panning) { vm.CancelInteraction(); _panStart = current.Position; _offsetBeforePan = _offset; }
        else vm.BeginInteraction(ToDocument(current.Position), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Control));
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Workspace is not { } vm) return;
        var point = e.GetPosition(this);
        if (_panning) { _offset = _offsetBeforePan + (point - _panStart); InvalidateVisual(); }
        else if (_pointer is not null) vm.UpdateInteraction(ToDocument(point), e.KeyModifiers.HasFlag(KeyModifiers.Shift), e.KeyModifiers.HasFlag(KeyModifiers.Alt));
        else { var cursor = ToDocument(point); vm.CursorPosition = $"{cursor.X:0.0}, {cursor.Y:0.0}"; }
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
        SetPanning(false);
        _pointer = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_pointer is not null) Workspace?.CancelInteraction();
        SetPanning(false);
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
        if (Workspace is not { } vm || e.Handled || _textEditor?.IsKeyboardFocusWithin == true) return;
        if (e.Key == Key.Escape && _panning)
        {
            _offset = _offsetBeforePan; SetPanning(false); _pointer?.Capture(null); _pointer = null;
            InvalidateVisual(); e.Handled = true; return;
        }
        if (e.Key == Key.Space && !vm.IsInteracting) { _space = true; e.Handled = true; return; }
        e.Handled = vm.TryHandleEditorShortcut(e.Key, e.KeyModifiers);
        if (e.Handled && e.Key == Key.Escape) { _pointer?.Capture(null); _pointer = null; }
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.Key == Key.Space) _space = false; }
    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        _space = false;
        if (_pointer is not null) { Workspace?.CancelInteraction(); SetPanning(false); _pointer.Capture(null); _pointer = null; }
    }
}
