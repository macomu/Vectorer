using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Vectorer.App.Controls;

/// <summary>
/// A <see cref="NumericUpDown"/> that avoids pushing <see langword="null"/> when the user
/// temporarily clears or types partial text, preserving the last valid value (or a clamped
/// default) and restoring the display text on commit/focus loss.
/// </summary>
public class SafeNumericUpDown : NumericUpDown
{
    public static readonly StyledProperty<bool> AllowEmptyProperty =
        AvaloniaProperty.Register<SafeNumericUpDown, bool>(nameof(AllowEmpty));

    public static readonly StyledProperty<decimal?> DefaultValueProperty =
        AvaloniaProperty.Register<SafeNumericUpDown, decimal?>(nameof(DefaultValue));

    private decimal? _lastValidValue;
    private bool _hasHadValue;
    private TextBox? _attachedTextBox;
    private bool _isUserEditingText;

    /// <summary>
    /// Allows <see langword="null"/> values pushed from the bound view-model (for example, mixed
    /// multi-selection states), while still preserving the last valid value during active editing.
    /// </summary>
    public bool AllowEmpty
    {
        get => GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    /// <summary>
    /// Optional fallback value used if empty input is committed before any valid value was seen.
    /// Defaults to <c>0</c> clamped to [<see cref="NumericUpDown.Minimum"/>, <see cref="NumericUpDown.Maximum"/>].
    /// </summary>
    public decimal? DefaultValue
    {
        get => GetValue(DefaultValueProperty);
        set => SetValue(DefaultValueProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_attachedTextBox is not null)
        {
            _attachedTextBox.TextChanged -= OnInnerTextChanged;
        }

        base.OnApplyTemplate(e);

        _attachedTextBox = e.NameScope.Find<TextBox>("PART_TextBox");
        if (_attachedTextBox is not null)
        {
            _attachedTextBox.TextChanged += OnInnerTextChanged;
        }
    }

    private void OnInnerTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (IsKeyboardFocusWithin)
        {
            _isUserEditingText = true;
        }
    }

    protected override decimal? OnCoerceValue(decimal? baseValue)
    {
        var coerced = base.OnCoerceValue(baseValue);
        if (coerced is { } valid)
        {
            _lastValidValue = valid;
            _hasHadValue = true;
            return valid;
        }

        if (AllowEmpty && !_isUserEditingText)
        {
            _lastValidValue = null;
            return null;
        }

        if (_lastValidValue is { } previous)
        {
            return ClampToRange(previous);
        }

        if (!AllowEmpty && (_isUserEditingText || _hasHadValue))
        {
            var fallback = GetFallbackValue();
            _lastValidValue = fallback;
            _hasHadValue = true;
            return fallback;
        }

        return null;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        try
        {
            base.OnLostFocus(e);
            if (!AllowEmpty && Value is null)
            {
                SetCurrentValue(ValueProperty, _lastValidValue ?? GetFallbackValue());
            }
            EnsureTextSyncedIfEmpty();
        }
        finally
        {
            _isUserEditingText = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            if (!AllowEmpty && Value is null)
            {
                SetCurrentValue(ValueProperty, _lastValidValue ?? GetFallbackValue());
            }
            EnsureTextSyncedIfEmpty();
            _isUserEditingText = false;
        }
    }

    private void EnsureTextSyncedIfEmpty()
    {
        if (_attachedTextBox is not null
            && string.IsNullOrWhiteSpace(_attachedTextBox.Text)
            && Value is { } current)
        {
            var formatted = current.ToString(FormatString, NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat);
            SetCurrentValue(TextProperty, formatted);
            _attachedTextBox.Text = formatted;
        }
    }

    private decimal GetFallbackValue()
    {
        var min = Math.Min(Minimum, Maximum);
        var max = Math.Max(Minimum, Maximum);
        return Math.Clamp(DefaultValue ?? 0m, min, max);
    }

    private decimal ClampToRange(decimal value)
    {
        var min = Math.Min(Minimum, Maximum);
        var max = Math.Max(Minimum, Maximum);
        return ClipValueToMinMax ? Math.Clamp(value, min, max) : value;
    }
}
