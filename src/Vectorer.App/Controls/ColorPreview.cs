using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Vectorer.App.Controls;

/// <summary>A color sample with transparency visible over a checkerboard.</summary>
public sealed class ColorPreview : Control
{
    public static readonly StyledProperty<Color> ColorProperty = AvaloniaProperty.Register<ColorPreview, Color>(nameof(Color), Colors.Transparent);
    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) { base.OnPropertyChanged(change); if (change.Property == ColorProperty) InvalidateVisual(); }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        ColorChannelSlider.DrawCheckerboard(context, rect);
        context.DrawRectangle(new SolidColorBrush(Color), new Pen(Brushes.Gray), rect);
    }
}
