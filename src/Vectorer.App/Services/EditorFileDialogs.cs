using Vectorer.App.Localization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using SkiaSharp;
using Vectorer.App.ViewModels;
using Avalonia.Platform.Storage;

namespace Vectorer.App.Services;

public sealed record EditorFile(string Name, Func<Task<Stream>> OpenRead, Func<Task<Stream>> OpenWrite, Uri? Location = null);

public enum UnsavedDecision { Cancel, Discard, Save }
public sealed record DocumentSettings(float Width, float Height, SKColor Background, string Title);

public interface IEditorFileDialogs
{
    Task<EditorFile?> OpenSvgAsync();
    Task<EditorFile?> ResolveSvgAsync(Uri location) => Task.FromResult<EditorFile?>(null);
    Task<EditorFile?> SaveSvgAsync(string suggestedName);
    Task<bool> ConfirmDiscardAsync();
    async Task<UnsavedDecision> ConfirmUnsavedAsync() => await ConfirmDiscardAsync() ? UnsavedDecision.Discard : UnsavedDecision.Cancel;
    Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew) => Task.FromResult<DocumentSettings?>(current);
    Task<bool> ShowRotateLayerDialogAsync(RotateLayerViewModel viewModel) => Task.FromResult(true);
    Task<bool> ShowPngExportDialogAsync(PngExportViewModel viewModel) => Task.FromResult(false);
    Task<EditorFile?> SavePngAsync(string suggestedName) => Task.FromResult<EditorFile?>(null);
    Task<string?> SelectExportFolderAsync() => Task.FromResult<string?>(null);
    Task ShowErrorAsync(string message) => Task.CompletedTask;
    Task ShowLocalizedErrorAsync(LocalizedMessage message, string fallback) => ShowErrorAsync(fallback);
}

/// <summary>Storage pickers and confirmation presentation only; document operations live in the VM.</summary>
public sealed class EditorFileDialogs(Window owner, AppLocalization localization) : IEditorFileDialogs
{
    private static readonly FilePickerFileType SvgType = new("SVG") { Patterns = ["*.svg"], MimeTypes = ["image/svg+xml"] };
    private static readonly FilePickerFileType PngType = new("PNG") { Patterns = ["*.png"], MimeTypes = ["image/png"] };
    private static EditorFile Adapt(IStorageFile file) => new(file.Name, file.OpenReadAsync, file.OpenWriteAsync, file.Path);
    public async Task<EditorFile?> ResolveSvgAsync(Uri location)
    {
        var file = await owner.StorageProvider.TryGetFileFromPathAsync(location);
        return file is null ? null : Adapt(file);
    }
    public async Task<EditorFile?> OpenSvgAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = localization.Get(nameof(Strings.Ui_OpenSvg)), AllowMultiple = false, FileTypeFilter = [SvgType] });
        return files.Count > 0 ? Adapt(files[0]) : null;
    }
    public async Task<EditorFile?> SaveSvgAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = localization.Get(nameof(Strings.Ui_SaveSvg)), SuggestedFileName = suggestedName, DefaultExtension = "svg", FileTypeChoices = [SvgType], ShowOverwritePrompt = true });
        return file is null ? null : Adapt(file);
    }
    public async Task<bool> ConfirmDiscardAsync() => await ConfirmUnsavedAsync() == UnsavedDecision.Discard;
    public async Task<UnsavedDecision> ConfirmUnsavedAsync()
    {
        using var text = new LocalizedControlBindings(localization);
        var dialog = text.Bind(new Window { Title = localization.Get(nameof(Strings.Ui_Unsaved)), Width = 460, Height = 165, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner }, Window.TitleProperty, nameof(Strings.Ui_Unsaved));
        var save = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Save)) }, Button.ContentProperty, nameof(Strings.Ui_Save)); var discard = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Discard)) }, Button.ContentProperty, nameof(Strings.Ui_Discard)); var cancel = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Cancel)) }, Button.ContentProperty, nameof(Strings.Ui_Cancel));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(discard); buttons.Children.Add(save);
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 20 };
        content.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_SaveQuestion)) }, TextBlock.TextProperty, nameof(Strings.Ui_SaveQuestion))); content.Children.Add(buttons); dialog.Content = content;
        save.Click += (_, _) => dialog.Close(UnsavedDecision.Save); discard.Click += (_, _) => dialog.Close(UnsavedDecision.Discard); cancel.Click += (_, _) => dialog.Close(UnsavedDecision.Cancel);
        return await dialog.ShowDialog<UnsavedDecision>(owner);
    }
    public async Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew)
    {
        using var text = new LocalizedControlBindings(localization);
        var vm = new DocumentSettingsViewModel { Width = (decimal)current.Width, Height = (decimal)current.Height, Title = current.Title,
            Transparent = current.Background.Alpha == 0, Background = current.Background.Alpha == 0 ? "#FFFFFF" : current.Background.ToString() };
        var dialog = text.Bind(new Window { Title = localization.Get(isNew ? nameof(Strings.Ui_NewDocument) : nameof(Strings.Ui_DocumentSettings)), Width = 440, Height = 510, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, DataContext = vm }, Window.TitleProperty, isNew ? nameof(Strings.Ui_NewDocument) : nameof(Strings.Ui_DocumentSettings));
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 10 };
        var title = new TextBox(); title.Bind(TextBox.TextProperty, new Binding(nameof(vm.Title)) { Mode = BindingMode.TwoWay });
        content.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_Title)) }, TextBlock.TextProperty, nameof(Strings.Ui_Title))); content.Children.Add(title);
        var presets = new ComboBox();
        text.BindChoices(presets, vm.Presets.Select(p => new LocalizedChoice(p, p.Name switch
        {
            "カスタム" => nameof(Strings.Ui_Custom),
            "A4 縦 (96dpi)" => nameof(Strings.Ui_PresetPortrait),
            "A4 横 (96dpi)" => nameof(Strings.Ui_PresetLandscape),
            _ => null
        }, localization)));
        presets.SelectedIndex = 0;
        presets.SelectionChanged += (_, _) => vm.ApplyPreset((presets.SelectedItem as LocalizedChoice)?.Value as CanvasPreset); content.Children.Add(presets);
        foreach (var field in new[] { (nameof(Strings.Ui_WidthPx), nameof(vm.Width)), (nameof(Strings.Ui_HeightPx), nameof(vm.Height)) })
        { content.Children.Add(text.Bind(new TextBlock { Text = localization.Get(field.Item1) }, TextBlock.TextProperty, field.Item1)); var input = new NumericUpDown { Minimum = 1, Maximum = 100000 }; input.Bind(NumericUpDown.ValueProperty, new Binding(field.Item2) { Mode = BindingMode.TwoWay }); content.Children.Add(input); }
        content.Children.Add(text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_SwapDimensions)), Command = vm.SwapDimensionsCommand }, Button.ContentProperty, nameof(Strings.Ui_SwapDimensions)));
        var transparent = text.Bind(new CheckBox { Content = localization.Get(nameof(Strings.Ui_Transparent)) }, CheckBox.ContentProperty, nameof(Strings.Ui_Transparent)); transparent.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.Transparent)) { Mode = BindingMode.TwoWay }); content.Children.Add(transparent);
        var background = new TextBox { Watermark = "#RRGGBB / #AARRGGBB" }; background.Bind(TextBox.TextProperty, new Binding(nameof(vm.Background)) { Mode = BindingMode.TwoWay }); content.Children.Add(background);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Cancel)) }, Button.ContentProperty, nameof(Strings.Ui_Cancel)); var apply = text.Bind(new Button { Content = localization.Get(isNew ? nameof(Strings.Ui_Create) : nameof(Strings.Ui_Apply)) }, Button.ContentProperty, isNew ? nameof(Strings.Ui_Create) : nameof(Strings.Ui_Apply));
        apply.Bind(Button.IsEnabledProperty, new Binding(nameof(vm.IsValid))); buttons.Children.Add(cancel); buttons.Children.Add(apply); content.Children.Add(buttons); dialog.Content = content;
        cancel.Click += (_, _) => dialog.Close(null); apply.Click += (_, _) => { if (vm.IsValid) dialog.Close(new DocumentSettings((float)vm.Width, (float)vm.Height, vm.BackgroundColor, vm.Title)); };
        return await dialog.ShowDialog<DocumentSettings?>(owner);
    }

    public async Task<bool> ShowRotateLayerDialogAsync(RotateLayerViewModel vm)
    {
        using var text = new LocalizedControlBindings(localization);
        var dialog = text.Bind(new Window
        {
            Title = localization.Get(nameof(Strings.Ui_LayerRotation)),
            Width = 420,
            Height = 290,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            DataContext = vm
        }, Window.TitleProperty, nameof(Strings.Ui_LayerRotation));

        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        content.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_RotationRange)) }, TextBlock.TextProperty, nameof(Strings.Ui_RotationRange)));

        var angleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110") };
        var slider = new Slider { Minimum = -360, Maximum = 360, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        slider.Bind(Slider.ValueProperty, new Binding(nameof(vm.Degrees)) { Mode = BindingMode.TwoWay });
        var numeric = new NumericUpDown { Minimum = -360, Maximum = 360, Increment = 5 };
        numeric.Bind(NumericUpDown.ValueProperty, new Binding(nameof(vm.Degrees)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(numeric, 1);
        angleRow.Children.Add(slider);
        angleRow.Children.Add(numeric);
        content.Children.Add(angleRow);

        content.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_RotationCenter)) }, TextBlock.TextProperty, nameof(Strings.Ui_RotationCenter)));
        var centerCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        text.BindChoices(centerCombo, vm.CenterOptions.Select(o => new LocalizedChoice(o,
            o.Mode == LayerRotationCenterMode.CanvasCenter ? nameof(Strings.Ui_CanvasCenter) : nameof(Strings.Ui_LayerCenter), localization)));
        centerCombo.Bind(ComboBox.SelectedValueProperty, new Binding(nameof(vm.SelectedCenterOption)) { Mode = BindingMode.TwoWay });
        content.Children.Add(centerCombo);

        var liveCheck = text.Bind(new CheckBox { Content = localization.Get(nameof(Strings.Ui_LivePreview)) }, CheckBox.ContentProperty, nameof(Strings.Ui_LivePreview));
        liveCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.LivePreview)) { Mode = BindingMode.TwoWay });
        content.Children.Add(liveCheck);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Cancel)) }, Button.ContentProperty, nameof(Strings.Ui_Cancel));
        var apply = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_ApplyRotation)) }, Button.ContentProperty, nameof(Strings.Ui_ApplyRotation));
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        content.Children.Add(buttons);
        dialog.Content = content;

        cancel.Click += (_, _) => dialog.Close(false);
        apply.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<EditorFile?> SavePngAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localization.Get(nameof(Strings.Ui_SavePng)),
            SuggestedFileName = suggestedName,
            DefaultExtension = "png",
            FileTypeChoices = [PngType],
            ShowOverwritePrompt = true
        });
        return file is null ? null : Adapt(file);
    }

    public async Task<string?> SelectExportFolderAsync()
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localization.Get(nameof(Strings.Ui_ExportFolder)),
            AllowMultiple = false
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ShowPngExportDialogAsync(PngExportViewModel vm)
    {
        using var text = new LocalizedControlBindings(localization);
        var dialog = text.Bind(new Window
        {
            Title = localization.Get(nameof(Strings.Ui_ExportPng)),
            Width = 680,
            Height = 470,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            DataContext = vm
        }, Window.TitleProperty, nameof(Strings.Ui_ExportPng));

        var root = new DockPanel { Margin = new Avalonia.Thickness(20) };

        // Mode radio buttons at top
        var modePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Avalonia.Thickness(0, 0, 0, 14) };
        modePanel.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_ExportMode)), FontWeight = Avalonia.Media.FontWeight.SemiBold }, TextBlock.TextProperty, nameof(Strings.Ui_ExportMode)));
        foreach (var (label, prop) in new[]
        {
            (nameof(Strings.Ui_WholeDocument), nameof(vm.IsDocumentMode)),
            (nameof(Strings.Ui_SelectionOnly), nameof(vm.IsSelectionMode)),
            (nameof(Strings.Ui_BatchLayers), nameof(vm.IsLayersBatchMode))
        })
        {
            var radio = text.Bind(new RadioButton { Content = localization.Get(label), GroupName = "PngExportModeGroup" }, RadioButton.ContentProperty, label);
            radio.Bind(RadioButton.IsCheckedProperty, new Binding(prop) { Mode = BindingMode.TwoWay });
            modePanel.Children.Add(radio);
        }
        DockPanel.SetDock(modePanel, Dock.Top);
        root.Children.Add(modePanel);

        // Bottom buttons
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 14, 0, 0)
        };
        var cancel = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Cancel)) }, Button.ContentProperty, nameof(Strings.Ui_Cancel));
        var exportBtn = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Export)) }, Button.ContentProperty, nameof(Strings.Ui_Export));
        exportBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(vm.CanExport)));
        buttons.Children.Add(cancel);
        buttons.Children.Add(exportBtn);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        // Main 2-column area
        var bodyGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,18,*") };

        // Left column: Settings
        var settingsStack = new StackPanel { Spacing = 10 };
        settingsStack.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_ExportSettings)), FontWeight = Avalonia.Media.FontWeight.SemiBold }, TextBlock.TextProperty, nameof(Strings.Ui_ExportSettings)));

        var baseRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        baseRow.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_BaseSize)) }, TextBlock.TextProperty, nameof(Strings.Ui_BaseSize)));
        var baseVal = new TextBlock { Foreground = Avalonia.Media.Brushes.LightSkyBlue };
        baseVal.Bind(TextBlock.TextProperty, new Binding(nameof(vm.BaseDimensionsLabel)));
        baseRow.Children.Add(baseVal);
        settingsStack.Children.Add(baseRow);

        var widthRow = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*,Auto") };
        widthRow.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_WidthPx)) }, TextBlock.TextProperty, nameof(Strings.Ui_WidthPx)));
        var widthInput = new NumericUpDown { Minimum = 1, Maximum = 32768, Increment = 10, FormatString = "0" };
        widthInput.Bind(NumericUpDown.ValueProperty, new Binding(nameof(vm.TargetWidth)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(widthInput, 1);
        widthRow.Children.Add(widthInput);
        var lockCheck = text.Bind(new CheckBox { Content = localization.Get(nameof(Strings.Ui_AspectLocked)), Margin = new Avalonia.Thickness(8, 0, 0, 0) }, CheckBox.ContentProperty, nameof(Strings.Ui_AspectLocked));
        lockCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.LockAspect)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(lockCheck, 2);
        widthRow.Children.Add(lockCheck);
        settingsStack.Children.Add(widthRow);

        var heightRow = new Grid { ColumnDefinitions = new ColumnDefinitions("90,*") };
        heightRow.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_HeightPx)) }, TextBlock.TextProperty, nameof(Strings.Ui_HeightPx)));
        var heightInput = new NumericUpDown { Minimum = 1, Maximum = 32768, Increment = 10, FormatString = "0" };
        heightInput.Bind(NumericUpDown.ValueProperty, new Binding(nameof(vm.TargetHeight)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(heightInput, 1);
        heightRow.Children.Add(heightInput);
        settingsStack.Children.Add(heightRow);

        var scaleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        scaleRow.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_Scale)) }, TextBlock.TextProperty, nameof(Strings.Ui_Scale)));
        foreach (var preset in new[] { "0.5", "1", "2", "4" })
        {
            var btn = new Button
            {
                Content = $"{preset}x",
                Command = vm.ApplyScalePresetFromTextCommand,
                CommandParameter = preset,
                Padding = new Avalonia.Thickness(8, 3)
            };
            scaleRow.Children.Add(btn);
        }
        var scaleStatus = new TextBlock { FontSize = 12, Foreground = Avalonia.Media.Brushes.Silver, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
        scaleStatus.Bind(TextBlock.TextProperty, new Binding(nameof(vm.ScaleStatusLabel)));
        scaleRow.Children.Add(scaleStatus);
        settingsStack.Children.Add(scaleRow);

        var transparentCheck = text.Bind(new CheckBox { Content = localization.Get(nameof(Strings.Ui_TransparentBackground)) }, CheckBox.ContentProperty, nameof(Strings.Ui_TransparentBackground));
        transparentCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.TransparentBackground)) { Mode = BindingMode.TwoWay });
        settingsStack.Children.Add(transparentCheck);

        var batchStack = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        batchStack.Bind(Avalonia.Visual.IsVisibleProperty, new Binding(nameof(vm.IsLayersBatchMode)));
        batchStack.Children.Add(text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_FilePrefix)) }, TextBlock.TextProperty, nameof(Strings.Ui_FilePrefix)));
        var prefixBox = new TextBox();
        prefixBox.Bind(TextBox.TextProperty, new Binding(nameof(vm.BatchFilePrefix)) { Mode = BindingMode.TwoWay });
        batchStack.Children.Add(prefixBox);
        var visibleOnlyCheck = text.Bind(new CheckBox { Content = localization.Get(nameof(Strings.Ui_VisibleLayersOnly)) }, CheckBox.ContentProperty, nameof(Strings.Ui_VisibleLayersOnly));
        visibleOnlyCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.VisibleLayersOnly)) { Mode = BindingMode.TwoWay });
        batchStack.Children.Add(visibleOnlyCheck);
        settingsStack.Children.Add(batchStack);

        Grid.SetColumn(settingsStack, 0);
        bodyGrid.Children.Add(settingsStack);

        // Right column: Preview
        var previewDock = new DockPanel();
        var previewHeader = text.Bind(new TextBlock { Text = localization.Get(nameof(Strings.Ui_Preview)), FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(0, 0, 0, 8) }, TextBlock.TextProperty, nameof(Strings.Ui_Preview));
        DockPanel.SetDock(previewHeader, Dock.Top);
        previewDock.Children.Add(previewHeader);

        var outSizeText = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
            FontSize = 12
        };
        outSizeText.Bind(TextBlock.TextProperty, new Binding(nameof(vm.OutputSizeLabel)));
        DockPanel.SetDock(outSizeText, Dock.Bottom);
        previewDock.Children.Add(outSizeText);

        var previewBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F172A")),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#30363D")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(8)
        };
        var previewImage = new Image { Stretch = Avalonia.Media.Stretch.Uniform };
        previewImage.Bind(Image.SourceProperty, new Binding(nameof(vm.PreviewBitmap)));
        previewBorder.Child = previewImage;
        previewDock.Children.Add(previewBorder);

        Grid.SetColumn(previewDock, 2);
        bodyGrid.Children.Add(previewDock);

        root.Children.Add(bodyGrid);
        dialog.Content = root;

        cancel.Click += (_, _) => dialog.Close(false);
        exportBtn.Click += (_, _) => { if (vm.CanExport) dialog.Close(true); };
        return await dialog.ShowDialog<bool>(owner);
    }

    public Task ShowErrorAsync(string message) => ShowErrorCoreAsync(message, null);
    public Task ShowLocalizedErrorAsync(LocalizedMessage message, string fallback) => ShowErrorCoreAsync(fallback, message);

    private async Task ShowErrorCoreAsync(string message, LocalizedMessage? localizedMessage)
    {
        using var text = new LocalizedControlBindings(localization);
        var dialog = text.Bind(new Window { Title = localization.Get(nameof(Strings.Ui_FileError)), Width = 520, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner }, Window.TitleProperty, nameof(Strings.Ui_FileError));
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 }; var close = text.Bind(new Button { Content = localization.Get(nameof(Strings.Ui_Close)), HorizontalAlignment = HorizontalAlignment.Right }, Button.ContentProperty, nameof(Strings.Ui_Close));
        var body = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        if (localizedMessage is not null) text.BindMessage(body, TextBlock.TextProperty, localizedMessage);
        content.Children.Add(body); content.Children.Add(close); dialog.Content = content;
        close.Click += (_, _) => dialog.Close(); await dialog.ShowDialog(owner);
    }
}
