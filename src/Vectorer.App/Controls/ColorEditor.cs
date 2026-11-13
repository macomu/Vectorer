using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Vectorer.App.Controls;

/// <summary>Shared RGBA / HSVA editor. All model changes are published through Color.</summary>
public sealed class ColorEditor : UserControl
{
    public static readonly StyledProperty<Color> ColorProperty = AvaloniaProperty.Register<ColorEditor, Color>(nameof(Color), Avalonia.Media.Colors.Black, defaultBindingMode: BindingMode.TwoWay);
    public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public event EventHandler? EditStarted;
    public event EventHandler? EditCompleted;
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "RGBA", "HSVA" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock[] _labels = new TextBlock[4];
    private readonly ColorChannelSlider[] _sliders = new ColorChannelSlider[4];
    private readonly ColorNumericUpDown[] _numbers = new ColorNumericUpDown[4];
    private readonly TextBox _hex = new() { Watermark = "#RRGGBB / #AARRGGBB" };
    private readonly ColorPreview _preview = new() { Width = 32, Height = 28 };
    private bool _syncing;
    private double _hue;
    private double _saturation;

    public ColorEditor()
    {
        var content = new StackPanel { Spacing = 3 };
        AutomationProperties.SetName(_mode, "RGBA / HSVA"); content.Children.Add(_mode);
        for (var i = 0; i < 4; i++)
        {
            var channel = i;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("20,*,78") };
            var label = _labels[i] = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            var slider = _sliders[i] = new ColorChannelSlider();
            var number = _numbers[i] = new ColorNumericUpDown { Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0.##", Padding = new Thickness(3, 2), ClipValueToMinMax = true };
            Grid.SetColumn(slider, 1); Grid.SetColumn(number, 2);
            row.Children.Add(label); row.Children.Add(slider); row.Children.Add(number); content.Children.Add(row);
            slider.EditStarted += (_, _) => EditStarted?.Invoke(this, EventArgs.Empty);
            slider.EditCompleted += (_, _) => EditCompleted?.Invoke(this, EventArgs.Empty);
            slider.PropertyChanged += (_, e) => { if (e.Property == ColorChannelSlider.ValueProperty && !_syncing) ChangeChannel(channel, slider.Value); };
            number.ValueChanged += (_, _) => { if (_syncing || number.Value is null) return; EditStarted?.Invoke(this, EventArgs.Empty); ChangeChannel(channel, (double)number.Value.Value); EditCompleted?.Invoke(this, EventArgs.Empty); };
        }
        var hexRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
        hexRow.Children.Add(new TextBlock { Text = "HEX", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_hex, 1); Grid.SetColumn(_preview, 2); hexRow.Children.Add(_hex); hexRow.Children.Add(_preview); content.Children.Add(hexRow);
        AutomationProperties.SetName(_hex, "HEX (#AARRGGBB)");
        _hex.LostFocus += (_, _) => CommitHex();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitHex(); e.Handled = true; } };
        _mode.SelectionChanged += (_, _) => Refresh();
        Content = content; Refresh();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColorProperty && _sliders[0] is not null) Refresh();
    }

    private void CommitHex()
    {
        if (Avalonia.Media.Color.TryParse(_hex.Text, out var color))
        {
            EditStarted?.Invoke(this, EventArgs.Empty); SetCurrentValue(ColorProperty, color); EditCompleted?.Invoke(this, EventArgs.Empty);
        }
        Refresh();
    }

    private void ChangeChannel(int channel, double value)
    {
        var color = Color;
        if (_mode.SelectedIndex == 1)
        {
            var (_, s, v) = ColorMath.ToHsv(color, _hue);
            if (v == 0) s = _saturation;
            if (channel == 0) _hue = value;
            if (channel == 1) s = _saturation = value / 100;
            if (channel == 2) v = value / 100;
            color = ColorMath.FromHsv(_hue, s, v, channel == 3 ? ColorMath.Byte(value / 100) : color.A);
        }
        else
        {
            var component = (byte)Math.Clamp(Math.Round(value), 0, 255);
            color = channel switch { 0 => Avalonia.Media.Color.FromArgb(color.A, component, color.G, color.B), 1 => Avalonia.Media.Color.FromArgb(color.A, color.R, component, color.B), 2 => Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, component), _ => Avalonia.Media.Color.FromArgb(component, color.R, color.G, color.B) };
        }
        SetCurrentValue(ColorProperty, color);
        Refresh(); // Also refresh hue changes while saturation/value is zero.
    }

    private void Refresh()
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            var color = Color;
            var hsv = ColorMath.ToHsv(color, _hue); _hue = hsv.H;
            if (hsv.V > 0) _saturation = hsv.S;
            var isHsv = _mode.SelectedIndex == 1;
            string[] names = isHsv ? ["H", "S", "V", "A"] : ["R", "G", "B", "A"];
            double[] values = isHsv ? [_hue, _saturation * 100, hsv.V * 100, color.A / 255d * 100] : [color.R, color.G, color.B, color.A];
            for (var i = 0; i < 4; i++)
            {
                var maximum = isHsv ? i == 0 ? 360 : 100 : 255;
                _labels[i].Text = names[i]; _sliders[i].Maximum = maximum; _sliders[i].Value = values[i]; _numbers[i].Maximum = maximum; _numbers[i].Value = (decimal)values[i];
                AutomationProperties.SetName(_sliders[i], names[i]); AutomationProperties.SetName(_numbers[i], names[i]);
                var colors = new List<Color>();
                for (var step = 0; step <= 24; step++)
                {
                    var t = step / 24d;
                    colors.Add(isHsv ? i switch
                    {
                        0 => ColorMath.FromHsv(t * 360, _saturation, hsv.V, color.A),
                        1 => ColorMath.FromHsv(_hue, t, hsv.V, color.A),
                        2 => ColorMath.FromHsv(_hue, _saturation, t, color.A),
                        _ => Avalonia.Media.Color.FromArgb(ColorMath.Byte(t), color.R, color.G, color.B)
                    } : i switch
                    {
                        0 => Avalonia.Media.Color.FromArgb(color.A, ColorMath.Byte(t), color.G, color.B), 1 => Avalonia.Media.Color.FromArgb(color.A, color.R, ColorMath.Byte(t), color.B),
                        2 => Avalonia.Media.Color.FromArgb(color.A, color.R, color.G, ColorMath.Byte(t)), _ => Avalonia.Media.Color.FromArgb(ColorMath.Byte(t), color.R, color.G, color.B)
                    });
                }
                _sliders[i].Colors = colors;
            }
            _hex.Text = color.A == 255 ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
            _preview.Color = color;
        }
        finally { _syncing = false; }
    }
}
