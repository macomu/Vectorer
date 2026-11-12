using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Vectorer.App.Localization;
using Vectorer.App.ViewModels;
using Vectorer.SamplePlugin;
using Xunit;

namespace Vectorer.App.Tests;

[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationCollection;

[Collection("Localization")]
public sealed class LocalizationPresentationTests
{
    [Fact]
    public void ExistingBindings_RetranslateDialogsChoicesAndPlugins_WithoutChangingEditorState()
    {
        var originalUi = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
            var vm = new MainWindowViewModel(l10n);
            var menu = Assert.Single(vm.PluginMenus);
            var tool = Assert.Single(vm.PluginToolbarItems);
            var shader = Assert.Single(vm.ShaderOptions, s => s.Id == SampleVectorerPlugin.SunsetWaveShaderId);
            vm.SelectedShaderOption = shader;
            var dirty = vm.HasUnsavedChanges;
            var document = vm.CurrentDocument;
            var selectedTool = vm.ActiveToolItem;
            var fillChoice = vm.FillChoices[1];
            var fillValue = vm.FillKind;
            var dashValue = vm.StrokeDash;
            var fillPicker = new ComboBox
            {
                ItemsSource = vm.FillChoices,
                SelectedValueBinding = new Binding(nameof(LocalizedChoice.Value))
            };
            using var selectionBinding = fillPicker.Bind(ComboBox.SelectedValueProperty,
                new Binding(nameof(MainWindowViewModel.FillKind)) { Source = vm, Mode = BindingMode.TwoWay });

            using var labels = new LocalizedControlBindings(l10n);
            var cancel = labels.Bind(new TextBlock(), TextBlock.TextProperty, nameof(AppLocalization.Ui_Cancel));
            var message = new LocalizedMessage(nameof(Strings.Status_FileOpenFailed), "test.svg");
            var error = labels.BindMessage(new TextBlock(), TextBlock.TextProperty, message);
            var pluginLabel = new TextBlock();
            using var pluginBinding = pluginLabel.Bind(TextBlock.TextProperty, new Binding(nameof(PluginMenuItem.Title)) { Source = menu });
            var shaderLabel = new TextBlock();
            using var shaderBinding = shaderLabel.Bind(TextBlock.TextProperty, new Binding(nameof(ShaderOptionItem.DisplayName)) { Source = shader });
            var choiceLabel = new TextBlock();
            using var choiceBinding = choiceLabel.Bind(TextBlock.TextProperty, new Binding(nameof(LocalizedChoice.Label)) { Source = fillChoice });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("キャンセル", cancel.Text);
            Assert.Equal("単色", choiceLabel.Text);
            Assert.Contains("サンセット", pluginLabel.Text);
            var selectedFill = fillPicker.SelectedItem;
            Assert.Same(fillChoice, selectedFill);

            vm.SetLanguageCommand.Execute("en");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Cancel", cancel.Text);
            Assert.Equal(l10n.Format(message), error.Text);
            Assert.Contains("Sunset Wave", pluginLabel.Text);
            Assert.Equal("Sunset Wave (SkSL)", shaderLabel.Text);
            Assert.Equal("Solid", choiceLabel.Text);
            Assert.Same(shader, vm.SelectedShaderOption);
            Assert.Same(document, vm.CurrentDocument);
            Assert.Same(selectedTool, vm.ActiveToolItem);
            Assert.Equal(dirty, vm.HasUnsavedChanges);
            Assert.Equal(fillValue, vm.FillKind);
            Assert.Equal(dashValue, vm.StrokeDash);
            Assert.Same(selectedFill, fillPicker.SelectedItem);

            vm.SetLanguageCommand.Execute("ja");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("キャンセル", cancel.Text);
            Assert.Contains("サンセット", pluginLabel.Text);
            Assert.Equal("単色", choiceLabel.Text);

            // Closed dialogs release their localization bindings.
            labels.Dispose();
            vm.SetLanguageCommand.Execute("en");
            Dispatcher.UIThread.RunJobs();
            Assert.NotEqual("Cancel", cancel.Text);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUi;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefault;
        }
    }
}
