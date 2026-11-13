using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Vectorer.App.Controls;

/// <summary>A component ramp with a marker below the ramp and pointer/keyboard editing.</summary>
public sealed class ColorChannelSlider : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ColorChannelSlider, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ColorChannelSlider, double>(nameof(Maximum), 255);
    public static readonly StyledProperty<IReadOnlyList<Color>> ColorsProperty = AvaloniaProperty.Register<ColorChannelSlider, IReadOnlyList<Color>>(nameof(Colors), []);
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IReadOnlyList<Color> Colors { get => GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }
    public event EventHandler? EditStarted;
    public event EventHandler? EditCompleted;
    private bool _dragging;

    public ColorChannelSlider() { Focusable = true; Height = 34; MinWidth = 70; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MaximumProperty || change.Property == ColorsProperty) InvalidateVisual();
    }

    internal static void DrawCheckerboard(DrawingContext context, Rect rect)
    {
        context.DrawRectangle(Brushes.White, null, rect);
        for (var y = rect.Top; y < rect.Bottom; y += 6)
            for (var x = rect.Left; x < rect.Right; x += 6)
                if (((int)((x - rect.Left) / 6) + (int)((y - rect.Top) / 6)) % 2 == 0)
                    context.DrawRectangle(Brushes.LightGray, null, new Rect(x, y, Math.Min(6, rect.Right - x), Math.Min(6, rect.Bottom - y)));
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bar = new Rect(7, 2, Math.Max(1, Bounds.Width - 14), 19);
        DrawCheckerboard(context, bar);
        if (Colors.Count > 0)
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            for (var i = 0; i < Colors.Count; i++) brush.GradientStops.Add(new Avalonia.Media.GradientStop(Colors[i], Colors.Count == 1 ? 0 : i / (double)(Colors.Count - 1)));
            context.DrawRectangle(brush, null, bar);
        }
        context.DrawRectangle(null, new Pen(IsFocused ? Brushes.DodgerBlue : Brushes.Gray, IsFocused ? 2 : 1), bar);
        var x = bar.Left + Math.Clamp(Maximum > 0 ? Value / Maximum : 0, 0, 1) * bar.Width;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open()) { path.BeginFigure(new Point(x, 22), true); path.LineTo(new Point(x - 5, 31)); path.LineTo(new Point(x + 5, 31)); path.EndFigure(true); }
        context.DrawGeometry(Brushes.White, new Pen(Brushes.Black), geometry);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); _dragging = true; EditStarted?.Invoke(this, EventArgs.Empty); e.Pointer.Capture(this); Update(e.GetPosition(this).X); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_dragging) { Update(e.GetPosition(this).X); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (_dragging) { Update(e.GetPosition(this).X); Complete(); e.Pointer.Capture(null); e.Handled = true; } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); Complete(); }
    private void Complete() { if (!_dragging) return; _dragging = false; EditCompleted?.Invoke(this, EventArgs.Empty); }
    private void Update(double x) => SetCurrentValue(ValueProperty, Math.Clamp((x - 7) / Math.Max(1, Bounds.Width - 14), 0, 1) * Maximum);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double? next = e.Key switch { Key.Left or Key.Down => Value - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1), Key.Right or Key.Up => Value + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1), Key.Home => 0, Key.End => Maximum, _ => null };
        if (next is null) return;
        EditStarted?.Invoke(this, EventArgs.Empty); SetCurrentValue(ValueProperty, Math.Clamp(next.Value, 0, Maximum)); EditCompleted?.Invoke(this, EventArgs.Empty); e.Handled = true;
    }
}
