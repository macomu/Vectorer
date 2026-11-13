using System.Collections.Specialized;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Controls;

/// <summary>The shared fill/stroke editor. Document mutations remain in PaintEditorViewModel.</summary>
public sealed class PaintEditor : UserControl
{
    private readonly WrapPanel _palette = new() { Orientation = Orientation.Horizontal };
    private PaintEditorViewModel? _viewModel;

    public PaintEditor()
    {
        MinWidth = 190;
        var content = new StackPanel { Spacing = 6 };
        var mode = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var none = Choice("Paint_None"); var solid = Choice("Paint_Solid"); var gradient = Choice("Paint_LinearGradient"); var texture = Choice("Paint_Texture");
        texture.Bind(IsVisibleProperty, new Binding("DataContext.SupportsTexture") { Source = this });
        mode.ItemsSource = new[] { none, solid, gradient, texture };
        mode.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding("ModeIndex") { Mode = BindingMode.TwoWay });
        BindText(mode, ToolTip.TipProperty, "Paint_Type");
        content.Children.Add(mode);
        var mixed = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        BindText(mixed, TextBlock.TextProperty, "Paint_Mixed"); mixed.Bind(IsVisibleProperty, new Binding("IsMixed")); content.Children.Add(mixed);
        var color = new ColorEditor(); color.Bind(ColorEditor.ColorProperty, new Binding("Color") { Mode = BindingMode.TwoWay });
        color.EditStarted += BeginEdit; color.EditCompleted += EndEdit; content.Children.Add(color);

        var gradientPanel = new StackPanel { Spacing = 5 };
        gradientPanel.Bind(IsVisibleProperty, new Binding("IsGradient"));
        var ramp = new GradientStopBar();
        ramp.Bind(GradientStopBar.StopsProperty, new Binding("GradientStops") { Mode = BindingMode.TwoWay });
        ramp.Bind(GradientStopBar.SelectedIndexProperty, new Binding("SelectedStopIndex") { Mode = BindingMode.TwoWay });
        BindText(ramp, ToolTip.TipProperty, "Paint_GradientHelp");
        ramp.EditStarted += BeginEdit; ramp.EditCompleted += EndEdit; gradientPanel.Children.Add(ramp);
        var position = new Grid { ColumnDefinitions = new ColumnDefinitions("*,92,Auto"), ColumnSpacing = 4 };
        var positionLabel = Label("Paint_Position"); var positionValue = Number("SelectedStopOffset", 0, 100); var remove = new Button { Content = "−", Padding = new Thickness(8, 3) };
        remove.Bind(Button.CommandProperty, new Binding("DeleteSelectedStopCommand")); BindText(remove, ToolTip.TipProperty, "Paint_RemoveStop"); BindText(remove, AutomationProperties.NameProperty, "Paint_RemoveStop");
        Grid.SetColumn(positionValue, 1); Grid.SetColumn(remove, 2); position.Children.Add(positionLabel); position.Children.Add(positionValue); position.Children.Add(remove); gradientPanel.Children.Add(position);
        var coordinates = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), ColumnSpacing = 4, RowSpacing = 3 };
        var xLabel = new TextBlock { Text = "X", HorizontalAlignment = HorizontalAlignment.Center }; var yLabel = new TextBlock { Text = "Y", HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(xLabel, 1); Grid.SetColumn(yLabel, 2); coordinates.Children.Add(xLabel); coordinates.Children.Add(yLabel);
        for (var row = 1; row <= 2; row++)
        {
            var label = Label(row == 1 ? "Paint_Start" : "Paint_End"); Grid.SetRow(label, row); coordinates.Children.Add(label);
            for (var col = 1; col <= 2; col++)
            {
                var field = Number((row == 1 ? "Start" : "End") + (col == 1 ? "X" : "Y"), -1000000, 1000000);
                AutomationProperties.SetName(field, (row == 1 ? "Start" : "End") + (col == 1 ? " X" : " Y"));
                Grid.SetRow(field, row); Grid.SetColumn(field, col); coordinates.Children.Add(field);
            }
        }
        gradientPanel.Children.Add(coordinates);
        var relativeUnits = Label("Paint_ObjectCoordinates"); relativeUnits.Bind(IsVisibleProperty, new Binding("UsesObjectBoundingBox")); gradientPanel.Children.Add(relativeUnits);
        var documentUnits = Label("Paint_UserCoordinates"); documentUnits.Bind(IsVisibleProperty, new Binding("UsesObjectBoundingBox") { Converter = BoolConverters.Not }); gradientPanel.Children.Add(documentUnits);
        var opacity = new Grid { ColumnDefinitions = new ColumnDefinitions("*,92"), ColumnSpacing = 4 };
        opacity.Children.Add(Label("Paint_Opacity")); var opacityValue = Number("GradientOpacity", 0, 100); Grid.SetColumn(opacityValue, 1); opacity.Children.Add(opacityValue); gradientPanel.Children.Add(opacity);
        content.Children.Add(gradientPanel);

        var paletteTitle = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        paletteTitle.Children.Add(Label("Paint_Presets"));
        var edit = new ToggleButton { Padding = new Thickness(7, 3) }; BindText(edit, ContentControl.ContentProperty, "Paint_EditPresets"); edit.Bind(ToggleButton.IsCheckedProperty, new Binding("IsEditingPalette") { Mode = BindingMode.TwoWay }); Grid.SetColumn(edit, 1); paletteTitle.Children.Add(edit); content.Children.Add(paletteTitle);
        content.Children.Add(new ScrollViewer { Content = _palette, Height = 63, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = content;
        DataContextChanged += (_, _) => AttachViewModel();
        AttachedToVisualTree += (_, _) => AttachViewModel();
        DetachedFromVisualTree += (_, _) => { if (_viewModel is not null) _viewModel.Palette.CollectionChanged -= PaletteChanged; _viewModel = null; };
    }

    private ComboBoxItem Choice(string key)
    {
        var choice = new ComboBoxItem();
        choice.Bind(ContentControl.ContentProperty, new Binding("DataContext.L10n." + key) { Source = this, Mode = BindingMode.OneWay });
        return choice;
    }
    private static TextBlock Label(string key) { var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap }; BindText(label, TextBlock.TextProperty, key); return label; }
    private static void BindText(AvaloniaObject control, AvaloniaProperty property, string key) => control.Bind(property, new Binding("L10n." + key) { Mode = BindingMode.OneWay });
    private ColorNumericUpDown Number(string property, decimal minimum, decimal maximum)
    {
        var field = new ColorNumericUpDown { Minimum = minimum, Maximum = maximum, Increment = 1, FormatString = "0.##", Padding = new Thickness(3, 2), ClipValueToMinMax = true };
        field.Bind(NumericUpDown.ValueProperty, new Binding(property) { Mode = BindingMode.TwoWay });
        return field;
    }
    private void BeginEdit(object? sender, EventArgs e) => (DataContext as PaintEditorViewModel)?.BeginEdit();
    private void EndEdit(object? sender, EventArgs e) => (DataContext as PaintEditorViewModel)?.EndEdit();
    private void AttachViewModel()
    {
        if (_viewModel is not null) _viewModel.Palette.CollectionChanged -= PaletteChanged;
        _viewModel = DataContext as PaintEditorViewModel;
        if (_viewModel is not null) _viewModel.Palette.CollectionChanged += PaletteChanged;
        RebuildPalette();
    }
    private void PaletteChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPalette();
    private void RebuildPalette()
    {
        _palette.Children.Clear();
        if (_viewModel is not { } vm) return;
        foreach (var preset in vm.Palette)
        {
            var sample = new ColorPreview { Color = preset.Color, Width = 28, Height = 28 };
            var button = new Button { Content = sample, Padding = new Thickness(2), Margin = new Thickness(2), Command = vm.SelectPresetCommand, CommandParameter = preset };
            ToolTip.SetTip(button, preset.Name); AutomationProperties.SetName(button, preset.Name);
            var panel = new StackPanel { Spacing = 0 }; panel.Children.Add(button);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            tools.Bind(IsVisibleProperty, new Binding("IsEditingPalette") { Source = vm });
            var previous = new Button { Content = "‹", Command = vm.MovePresetUpCommand, CommandParameter = preset, Padding = new Thickness(2, 0), MinHeight = 20 };
            var delete = new Button { Content = "×", Command = vm.RemovePresetCommand, CommandParameter = preset, Padding = new Thickness(2, 0), MinHeight = 20 };
            var next = new Button { Content = "›", Command = vm.MovePresetDownCommand, CommandParameter = preset, Padding = new Thickness(2, 0), MinHeight = 20 };
            BindText(previous, ToolTip.TipProperty, "Paint_MoveEarlier"); BindText(delete, ToolTip.TipProperty, "Paint_RemovePreset"); BindText(next, ToolTip.TipProperty, "Paint_MoveLater");
            tools.Children.Add(previous); tools.Children.Add(delete); tools.Children.Add(next); panel.Children.Add(tools); _palette.Children.Add(panel);
        }
        var add = new Button { Content = "+", Width = 36, Height = 36, Margin = new Thickness(2), Command = vm.AddPresetCommand };
        BindText(add, ToolTip.TipProperty, "Paint_AddPreset"); BindText(add, AutomationProperties.NameProperty, "Paint_AddPreset"); _palette.Children.Add(add);
    }
}
