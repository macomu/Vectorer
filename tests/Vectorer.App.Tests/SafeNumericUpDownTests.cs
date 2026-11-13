using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vectorer.App.Controls;
using Vectorer.App.Converters;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class SafeNumericUpDownTests
{
    private static readonly MethodInfo CommitInputMethod =
        typeof(NumericUpDown).GetMethod("CommitInput", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo OnLostFocusMethod =
        typeof(SafeNumericUpDown).GetMethod("OnLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly FieldInfo IsUserEditingTextField =
        typeof(SafeNumericUpDown).GetField("_isUserEditingText", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static (SafeNumericUpDown Control, TextBox TextBox) CreateControl(bool allowEmpty = false, decimal minimum = 0m, decimal maximum = 1000m)
    {
        TextBox? inner = null;
        var control = new SafeNumericUpDown
        {
            AllowEmpty = allowEmpty,
            Minimum = minimum,
            Maximum = maximum,
            Template = new FuncControlTemplate<NumericUpDown>((_, scope) =>
            {
                var box = new TextBox { Name = "PART_TextBox" };
                scope.Register("PART_TextBox", box);
                inner = box;
                return box;
            })
        };
        control.ApplyTemplate();
        Assert.NotNull(inner);
        return (control, inner!);
    }

    private static void Commit(SafeNumericUpDown control, bool forceTextUpdate) =>
        CommitInputMethod.Invoke(control, [forceTextUpdate]);

    [Fact]
    public void ClearingText_MaintainsPreviousValueWithoutValidationError_AndRestoresTextOnCommit()
    {
        var vm = new MainWindowViewModel();
        var (control, textBox) = CreateControl();
        control.DataContext = vm;
        using var sub = control.Bind(NumericUpDown.ValueProperty, new Binding(nameof(MainWindowViewModel.CornerRadius)) { Mode = BindingMode.TwoWay });

        vm.CornerRadius = 12m;
        Commit(control, true);
        Assert.Equal(12m, control.Value);
        Assert.Equal("12", textBox.Text);
        Assert.False(DataValidationErrors.GetHasErrors(control));

        // Temporarily clearing the box leaves text empty while keeping previous valid value.
        IsUserEditingTextField.SetValue(control, true);
        textBox.Text = "";
        Commit(control, false);
        Assert.Equal(12m, control.Value);
        Assert.Equal(12m, vm.CornerRadius);
        Assert.Equal("", textBox.Text);
        Assert.False(DataValidationErrors.GetHasErrors(control));

        // Typing a new value updates the ViewModel cleanly.
        textBox.Text = "24";
        Commit(control, false);
        Assert.Equal(24m, control.Value);
        Assert.Equal(24m, vm.CornerRadius);
        Assert.False(DataValidationErrors.GetHasErrors(control));

        // Leaving the box empty on focus loss restores the maintained value text.
        IsUserEditingTextField.SetValue(control, true);
        textBox.Text = "";
        Commit(control, false);
        OnLostFocusMethod.Invoke(control, [new RoutedEventArgs(InputElement.LostFocusEvent)]);
        Assert.Equal(24m, control.Value);
        Assert.Equal(24m, vm.CornerRadius);
        Assert.Equal("24", textBox.Text);
        Assert.False(DataValidationErrors.GetHasErrors(control));
    }

    [Fact]
    public void EmptyCommitWithoutPriorValue_FallsBackToClampedDefault()
    {
        var (control, textBox) = CreateControl(allowEmpty: false, minimum: 3m, maximum: 64m);
        IsUserEditingTextField.SetValue(control, true);
        textBox.Text = "";
        Commit(control, true);
        OnLostFocusMethod.Invoke(control, [new RoutedEventArgs(InputElement.LostFocusEvent)]);

        Assert.Equal(3m, control.Value);
        Assert.Equal("3", textBox.Text);
        Assert.False(DataValidationErrors.GetHasErrors(control));
    }

    [Fact]
    public void AllowEmpty_PreservesMixedNullFromViewModel_AndMaintainsPreviousValueDuringUserEdit()
    {
        var vm = new MainWindowViewModel();
        var (control, textBox) = CreateControl(allowEmpty: true, minimum: 0m, maximum: 100m);
        control.DataContext = vm;
        using var numSub = control.Bind(NumericUpDown.ValueProperty, new Binding(nameof(MainWindowViewModel.FillOpacity)) { Mode = BindingMode.TwoWay });

        var slider = new Slider { Minimum = 0, Maximum = 100, DataContext = vm };
        using var sliderSub = slider.Bind(RangeBase.ValueProperty, new Binding(nameof(MainWindowViewModel.FillOpacity))
        {
            Mode = BindingMode.TwoWay,
            Converter = NullableDecimalToDoubleConverter.Instance
        });

        vm.FillOpacity = 80m;
        textBox.Text = "80";
        Commit(control, true);
        Assert.Equal(80m, control.Value);
        Assert.Equal(80d, slider.Value);
        Assert.False(DataValidationErrors.GetHasErrors(slider));

        // Active user clearing keeps 80 instead of pushing null to the VM or Slider.
        IsUserEditingTextField.SetValue(control, true);
        textBox.Text = "";
        Commit(control, false);
        Assert.Equal(80m, control.Value);
        Assert.Equal(80m, vm.FillOpacity);
        Assert.Equal(80d, slider.Value);
        Assert.False(DataValidationErrors.GetHasErrors(control));
        Assert.False(DataValidationErrors.GetHasErrors(slider));

        // Invalid partial input on focus loss also restores the last valid value.
        textBox.Text = "-";
        Commit(control, true);
        OnLostFocusMethod.Invoke(control, [new RoutedEventArgs(InputElement.LostFocusEvent)]);
        Assert.Equal(80m, control.Value);
        Assert.Equal("80", textBox.Text);

        // Programmatic null (mixed multi-selection) clears Value without raising validation errors.
        vm.FillOpacity = null;
        Assert.Null(control.Value);
        Assert.False(DataValidationErrors.GetHasErrors(control));
        Assert.False(DataValidationErrors.GetHasErrors(slider));
    }
}
