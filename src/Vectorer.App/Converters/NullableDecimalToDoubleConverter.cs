using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Vectorer.App.Converters;

/// <summary>
/// Converts <see cref="decimal"/>? to <see cref="double"/> for <see cref="Avalonia.Controls.Slider"/>
/// bindings so <see langword="null"/> (mixed multi-selection) does not raise an <see cref="InvalidCastException"/>.
/// </summary>
public sealed class NullableDecimalToDoubleConverter : IValueConverter
{
    public static readonly NullableDecimalToDoubleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal d ? (double)d : BindingOperations.DoNothing;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d && double.IsFinite(d)
            ? (decimal?)Math.Round((decimal)d, 4)
            : BindingOperations.DoNothing;
}
