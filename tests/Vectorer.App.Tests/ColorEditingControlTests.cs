using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using Vectorer.App.Controls;
using Vectorer.App.Localization;
using Vectorer.App.ViewModels;
using Vectorer.Abstractions.Styling;
using Xunit;
using ColorStop = Vectorer.Abstractions.Styling.GradientStop;

namespace Vectorer.App.Tests;

public sealed class ColorEditingControlTests
{
    [Theory]
    [InlineData(255, 0, 0, 0)]
    [InlineData(0, 255, 0, 120)]
    [InlineData(0, 0, 255, 240)]
    [InlineData(255, 0, 255, 300)]
    public void HsvConversion_RoundTripsPrimaryColorsAndAlpha(int r, int g, int b, int expectedHue)
    {
        var color = Color.FromArgb(127, (byte)r, (byte)g, (byte)b);
        var hsv = ColorMath.ToHsv(color);
        Assert.Equal(expectedHue, hsv.H, 5);
        Assert.Equal(color, ColorMath.FromHsv(hsv.H, hsv.S, hsv.V, color.A));
    }

    [Fact]
    public void AchromaticColors_RetainHueAndHsvValuesAreClamped()
    {
        var gray = ColorMath.ToHsv(Colors.Gray, 217);
        var black = ColorMath.ToHsv(Colors.Black, 217);
        Assert.Equal(217, gray.H); Assert.Equal(217, black.H);
        Assert.Equal(0, gray.S); Assert.Equal(0, black.V);
        Assert.Equal(Colors.Blue, ColorMath.FromHsv(-120, 2, 2));
        Assert.Equal(Colors.Red, ColorMath.FromHsv(720, 1, 1));
    }

    [Fact]
    public void GradientInsertion_SamplesCurrentColorAndAlphaAndHonorsHardEdges()
    {
        ColorStop[] stops = [new(0, new SKColor(255, 0, 0, 255)), new(1, new SKColor(0, 0, 255, 0))];
        Assert.Equal(new SKColor(128, 0, 128, 128), GradientStopBar.Sample(stops, .5f));
        Assert.Equal(stops[0].Color, GradientStopBar.Sample(stops, -1));
        Assert.Equal(stops[1].Color, GradientStopBar.Sample(stops, 2));
        ColorStop[] hardEdge = [new(0, SKColors.Red), new(.5f, SKColors.Red), new(.5f, SKColors.Blue), new(1, SKColors.Blue)];
        Assert.Equal(SKColors.Blue, GradientStopBar.Sample(hardEdge, .5f));
        Assert.Equal(SKColors.Red, GradientStopBar.Sample(hardEdge, .49f));
    }

    [Fact]
    public void ColorEditor_UsesSafeNumericFieldsAndSynchronizesExternalColor()
    {
        var editor = new ColorEditor { Color = Color.FromArgb(64, 10, 20, 30) };
        var panel = Assert.IsType<StackPanel>(editor.Content);
        var fields = panel.Children.OfType<Grid>().Take(4).SelectMany(g => g.Children.OfType<ColorNumericUpDown>()).ToArray();
        Assert.Equal(4, fields.Length);
        Assert.Equal(new decimal?[] { 10, 20, 30, 64 }, fields.Select(f => f.Value));
        var starts = 0; var completes = 0; editor.EditStarted += (_, _) => starts++; editor.EditCompleted += (_, _) => completes++;
        fields[0].Value = 200;
        Assert.Equal(Color.FromArgb(64, 200, 20, 30), editor.Color);
        Assert.Equal(1, starts); Assert.Equal(1, completes);
        Assert.Equal(255, fields[0].Maximum);
    }

    [Fact]
    public void NumericComponent_TemporaryEmptyValueRetainsPreviousComponent()
    {
        var field = new ColorNumericUpDown { Minimum = 0, Maximum = 255, Value = 120 };
        field.Value = null;
        Assert.Equal(120m, field.Value);
        field.Value = 80;
        field.Value = null;
        Assert.Equal(80m, field.Value);
        Assert.Equal(0, field.MinWidth);
        Assert.False(field.ShowButtonSpinner);
    }

    [Fact]
    public void FourPaintViews_InitialBindingAndLocaleChangesPreserveStylesAndUndoState()
    {
        var l10n = new AppLocalization(initialLanguage: "en");
        var vm = new MainWindowViewModel(l10n);
        var fill = vm.DefaultCreationStyle.Fill;
        var stroke = vm.DefaultCreationStyle.Stroke;
        var document = new Vectorer.Svg.SvgSerializer().SerializeToString(vm.CurrentDocument);
        PaintEditor[] views = [new() { DataContext = vm.FillPaintEditor }, new() { DataContext = vm.StrokePaintEditor }, new() { DataContext = vm.FillPaintEditor }, new() { DataContext = vm.StrokePaintEditor }];
        foreach (var view in views)
        {
            var panel = Assert.IsType<StackPanel>(view.Content);
            var mode = Assert.IsType<ComboBox>(panel.Children[0]);
            Assert.Equal(1, mode.SelectedIndex);
            var choices = Assert.IsAssignableFrom<IEnumerable<ComboBoxItem>>(mode.ItemsSource).ToArray();
            Assert.Equal("Solid color", choices[1].Content);
        }
        l10n.ApplyCulture("ja", System.Globalization.CultureInfo.GetCultureInfo("ja"));
        foreach (var view in views)
        {
            var mode = Assert.IsType<ComboBox>(Assert.IsType<StackPanel>(view.Content).Children[0]);
            var choices = Assert.IsAssignableFrom<IEnumerable<ComboBoxItem>>(mode.ItemsSource).ToArray();
            Assert.Equal("単色", choices[1].Content);
            Assert.Equal(1, mode.SelectedIndex);
        }
        Assert.Equal(fill, vm.DefaultCreationStyle.Fill);
        Assert.Equal(stroke, vm.DefaultCreationStyle.Stroke);
        Assert.Equal(document, new Vectorer.Svg.SvgSerializer().SerializeToString(vm.CurrentDocument));
        Assert.False(vm.HasUnsavedChanges);
        vm.UndoCommand.Execute(null);
        Assert.Equal(fill, vm.DefaultCreationStyle.Fill);
        Assert.Equal(stroke, vm.DefaultCreationStyle.Stroke);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void PaintView_RequestedLocalizationKeysExistInBothResourcesAndBindingAccessors()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Vectorer.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "Vectorer.App", "Controls", "PaintEditor.cs"));
        var keys = System.Text.RegularExpressions.Regex.Matches(source, "\"(Paint_[A-Za-z]+)\"")
            .Select(match => match.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(keys);
        Assert.Contains("Paint_UserCoordinates", keys);
        foreach (var file in new[] { "Strings.resx", "Strings.ja.resx" })
        {
            var resource = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName, "src", "Vectorer.App", "Localization", file));
            var entries = resource.Root!.Elements("data").ToDictionary(element => element.Attribute("name")!.Value, element => element.Element("value")!.Value);
            foreach (var key in keys)
            {
                Assert.True(entries.TryGetValue(key, out var value), $"{file} is missing {key}");
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.NotNull(typeof(AppLocalization).GetProperty(key));
            }
        }
    }
}
