using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using Vectorer.Abstractions.Styling;
using ColorStop = Vectorer.Abstractions.Styling.GradientStop;

namespace Vectorer.App.Controls;

/// <summary>Editable gradient ramp. Published stop arrays are replaced, never mutated.</summary>
public sealed class GradientStopBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<ColorStop>> StopsProperty = AvaloniaProperty.Register<GradientStopBar, IReadOnlyList<ColorStop>>(nameof(Stops), [], defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<GradientStopBar, int>(nameof(SelectedIndex), 0, defaultBindingMode: BindingMode.TwoWay);
    public IReadOnlyList<ColorStop> Stops { get => GetValue(StopsProperty); set => SetValue(StopsProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public event EventHandler? EditStarted;
    public event EventHandler? EditCompleted;
    private bool _dragging;
    private double _grabOffset;

    public GradientStopBar() { Focusable = true; Height = 49; MinWidth = 100; }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StopsProperty || change.Property == SelectedIndexProperty) InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var bar = new Rect(8, 3, Math.Max(1, Bounds.Width - 16), 23);
        ColorChannelSlider.DrawCheckerboard(context, bar);
        if (Stops.Count > 0)
        {
            var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
            foreach (var stop in Stops.OrderBy(s => s.Offset)) brush.GradientStops.Add(new Avalonia.Media.GradientStop(ToColor(stop.Color), Math.Clamp(stop.Offset, 0, 1)));
            context.DrawRectangle(brush, null, bar);
        }
        context.DrawRectangle(null, new Pen(IsFocused ? Brushes.DodgerBlue : Brushes.Gray), bar);
        // Draw selected marker last so coincident stops remain discoverable.
        foreach (var i in Enumerable.Range(0, Stops.Count).OrderBy(i => i == SelectedIndex ? 1 : 0))
        {
            var rect = HandleRect(i);
            ColorChannelSlider.DrawCheckerboard(context, rect);
            context.DrawRectangle(new SolidColorBrush(ToColor(Stops[i].Color)), new Pen(i == SelectedIndex ? Brushes.DodgerBlue : Brushes.Black, i == SelectedIndex ? 3 : 1), rect);
        }
    }
    private Rect HandleRect(int index) => new(8 + Math.Clamp(Stops[index].Offset, 0, 1) * Math.Max(1, Bounds.Width - 16) - 6, 31, 12, 13);
    private float Offset(double x) => (float)Math.Clamp((x - 8) / Math.Max(1, Bounds.Width - 16), 0, 1);
    private static Color ToColor(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); var point = e.GetPosition(this);
        var hit = SelectedIndex >= 0 && SelectedIndex < Stops.Count && HandleRect(SelectedIndex).Inflate(3).Contains(point) ? SelectedIndex
            : Enumerable.Range(0, Stops.Count).Where(i => HandleRect(i).Inflate(3).Contains(point)).DefaultIfEmpty(-1).First();
        if (hit >= 0)
        {
            SetCurrentValue(SelectedIndexProperty, hit); _grabOffset = point.X - HandleRect(hit).Center.X; _dragging = true; EditStarted?.Invoke(this, EventArgs.Empty); e.Pointer.Capture(this);
        }
        else if (e.ClickCount == 2 && !Enumerable.Range(0, Stops.Count).Any(i => Math.Abs(point.X - HandleRect(i).Center.X) <= 8))
        {
            var offset = Offset(point.X);
            EditStarted?.Invoke(this, EventArgs.Empty);
            var added = new ColorStop(offset, Sample(Stops, offset));
            var stops = Stops.Append(added).OrderBy(s => s.Offset).ToArray();
            SetCurrentValue(StopsProperty, stops); SetCurrentValue(SelectedIndexProperty, Array.FindIndex(stops, s => ReferenceEquals(s, added)));
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_dragging) { MoveStop(Offset(e.GetPosition(this).X - _grabOffset)); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); if (_dragging) { MoveStop(Offset(e.GetPosition(this).X - _grabOffset)); Complete(); e.Pointer.Capture(null); e.Handled = true; } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); Complete(); }
    private void Complete() { if (!_dragging) return; _dragging = false; EditCompleted?.Invoke(this, EventArgs.Empty); }
    private void MoveStop(float offset)
    {
        if (SelectedIndex < 0 || SelectedIndex >= Stops.Count) return;
        var moved = Stops[SelectedIndex] with { Offset = offset };
        var sorted = Stops.Select((s, i) => i == SelectedIndex ? moved : s).OrderBy(s => s.Offset).ToArray();
        SetCurrentValue(StopsProperty, sorted); SetCurrentValue(SelectedIndexProperty, Array.FindIndex(sorted, s => ReferenceEquals(s, moved)));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (SelectedIndex < 0 || SelectedIndex >= Stops.Count) return;
        if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            var delta = e.Key is Key.Up or Key.PageUp ? -1 : 1;
            SetCurrentValue(SelectedIndexProperty, (SelectedIndex + delta + Stops.Count) % Stops.Count); e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back && Stops.Count > 2)
        {
            EditStarted?.Invoke(this, EventArgs.Empty);
            SetCurrentValue(StopsProperty, Stops.Where((_, i) => i != SelectedIndex).ToArray());
            SetCurrentValue(SelectedIndexProperty, Math.Min(SelectedIndex, Stops.Count - 1));
            EditCompleted?.Invoke(this, EventArgs.Empty); e.Handled = true;
        }
        else if (e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            var offset = e.Key switch { Key.Home => 0, Key.End => 1, Key.Left => Stops[SelectedIndex].Offset - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1f : .01f), _ => Stops[SelectedIndex].Offset + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1f : .01f) };
            EditStarted?.Invoke(this, EventArgs.Empty); MoveStop(Math.Clamp(offset, 0, 1)); EditCompleted?.Invoke(this, EventArgs.Empty); e.Handled = true;
        }
    }

    /// <summary>Samples straight sRGB components, matching the renderer gradient interpolation.</summary>
    public static SKColor Sample(IReadOnlyList<ColorStop> stops, float offset)
    {
        if (stops.Count == 0) return SKColors.Transparent;
        var ordered = stops.OrderBy(s => s.Offset).ToArray();
        if (offset < ordered[0].Offset) return ordered[0].Color;
        var left = ordered[0];
        foreach (var right in ordered.Skip(1))
        {
            if (offset < right.Offset)
            {
                var t = Math.Clamp((offset - left.Offset) / (right.Offset - left.Offset), 0, 1);
                var alpha = left.Color.Alpha + (right.Color.Alpha - left.Color.Alpha) * t;
                byte Channel(byte a, byte b) => (byte)Math.Clamp(Math.Round(a + (b - a) * t), 0, 255);
                return new SKColor(Channel(left.Color.Red, right.Color.Red), Channel(left.Color.Green, right.Color.Green), Channel(left.Color.Blue, right.Color.Blue), (byte)Math.Round(alpha));
            }
            left = right;
        }
        return left.Color;
    }
}
