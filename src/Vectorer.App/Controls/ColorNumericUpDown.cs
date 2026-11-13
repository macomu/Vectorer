using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Vectorer.App.Controls;

/// <summary>Retains the current component while numeric text is temporarily incomplete.</summary>
public sealed class ColorNumericUpDown : NumericUpDown
{
    private decimal? _lastValue;
    public ColorNumericUpDown() { MinWidth = 0; ShowButtonSpinner = false; }
    protected override Type StyleKeyOverride => typeof(NumericUpDown);
    protected override decimal? OnCoerceValue(decimal? baseValue)
    {
        var value = base.OnCoerceValue(baseValue);
        if (value is { } valid) return _lastValue = valid;
        return Math.Clamp(_lastValue ?? 0, Math.Min(Minimum, Maximum), Math.Max(Minimum, Maximum));
    }
    protected override void OnLostFocus(RoutedEventArgs e) { base.OnLostFocus(e); RestoreText(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.Key == Key.Enter) RestoreText(); }
    private void RestoreText()
    {
        if (Value is { } value) SetCurrentValue(TextProperty, value.ToString(FormatString, NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat));
    }
}
