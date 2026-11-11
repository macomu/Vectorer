using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Data;
using SkiaSharp;
using Vectorer.App.ViewModels;
using Avalonia.Platform.Storage;

namespace Vectorer.App.Services;

public sealed record EditorFile(string Name, Func<Task<Stream>> OpenRead, Func<Task<Stream>> OpenWrite);

public enum UnsavedDecision { Cancel, Discard, Save }
public sealed record DocumentSettings(float Width, float Height, SKColor Background, string Title);

public interface IEditorFileDialogs
{
    Task<EditorFile?> OpenSvgAsync();
    Task<EditorFile?> SaveSvgAsync(string suggestedName);
    Task<bool> ConfirmDiscardAsync();
    async Task<UnsavedDecision> ConfirmUnsavedAsync() => await ConfirmDiscardAsync() ? UnsavedDecision.Discard : UnsavedDecision.Cancel;
    Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew) => Task.FromResult<DocumentSettings?>(current);
    Task<bool> ShowRotateLayerDialogAsync(RotateLayerViewModel viewModel) => Task.FromResult(true);
    Task ShowErrorAsync(string message) => Task.CompletedTask;
}

/// <summary>Storage pickers and confirmation presentation only; document operations live in the VM.</summary>
public sealed class EditorFileDialogs(Window owner) : IEditorFileDialogs
{
    private static readonly FilePickerFileType SvgType = new("SVG") { Patterns = ["*.svg"], MimeTypes = ["image/svg+xml"] };
    private static EditorFile Adapt(IStorageFile file) => new(file.Name, file.OpenReadAsync, file.OpenWriteAsync);
    public async Task<EditorFile?> OpenSvgAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "SVGを開く", AllowMultiple = false, FileTypeFilter = [SvgType] });
        return files.Count > 0 ? Adapt(files[0]) : null;
    }
    public async Task<EditorFile?> SaveSvgAsync(string suggestedName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "SVGを保存", SuggestedFileName = suggestedName, DefaultExtension = "svg", FileTypeChoices = [SvgType], ShowOverwritePrompt = true });
        return file is null ? null : Adapt(file);
    }
    public async Task<bool> ConfirmDiscardAsync() => await ConfirmUnsavedAsync() == UnsavedDecision.Discard;
    public async Task<UnsavedDecision> ConfirmUnsavedAsync()
    {
        var dialog = new Window { Title = "未保存の変更", Width = 460, Height = 165, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var save = new Button { Content = "保存" }; var discard = new Button { Content = "保存しない" }; var cancel = new Button { Content = "キャンセル" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(discard); buttons.Children.Add(save);
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 20 };
        content.Children.Add(new TextBlock { Text = "未保存の変更を保存しますか？" }); content.Children.Add(buttons); dialog.Content = content;
        save.Click += (_, _) => dialog.Close(UnsavedDecision.Save); discard.Click += (_, _) => dialog.Close(UnsavedDecision.Discard); cancel.Click += (_, _) => dialog.Close(UnsavedDecision.Cancel);
        return await dialog.ShowDialog<UnsavedDecision>(owner);
    }
    public async Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew)
    {
        var vm = new DocumentSettingsViewModel { Width = (decimal)current.Width, Height = (decimal)current.Height, Title = current.Title,
            Transparent = current.Background.Alpha == 0, Background = current.Background.Alpha == 0 ? "#FFFFFF" : current.Background.ToString() };
        var dialog = new Window { Title = isNew ? "新規ドキュメント" : "ドキュメント設定", Width = 440, Height = 510, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, DataContext = vm };
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 10 };
        var title = new TextBox(); title.Bind(TextBox.TextProperty, new Binding(nameof(vm.Title)) { Mode = BindingMode.TwoWay });
        content.Children.Add(new TextBlock { Text = "タイトル" }); content.Children.Add(title);
        var presets = new ComboBox { ItemsSource = vm.Presets, SelectedIndex = 0 };
        presets.SelectionChanged += (_, _) => vm.ApplyPreset(presets.SelectedItem as CanvasPreset); content.Children.Add(presets);
        foreach (var field in new[] { ("幅 (px)", nameof(vm.Width)), ("高さ (px)", nameof(vm.Height)) })
        { content.Children.Add(new TextBlock { Text = field.Item1 }); var input = new NumericUpDown { Minimum = 1, Maximum = 100000 }; input.Bind(NumericUpDown.ValueProperty, new Binding(field.Item2) { Mode = BindingMode.TwoWay }); content.Children.Add(input); }
        content.Children.Add(new Button { Content = "縦横を入れ替え", Command = vm.SwapDimensionsCommand });
        var transparent = new CheckBox { Content = "背景を透過" }; transparent.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.Transparent)) { Mode = BindingMode.TwoWay }); content.Children.Add(transparent);
        var background = new TextBox { Watermark = "#RRGGBB / #AARRGGBB" }; background.Bind(TextBox.TextProperty, new Binding(nameof(vm.Background)) { Mode = BindingMode.TwoWay }); content.Children.Add(background);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "キャンセル" }; var apply = new Button { Content = isNew ? "作成" : "適用" };
        apply.Bind(Button.IsEnabledProperty, new Binding(nameof(vm.IsValid))); buttons.Children.Add(cancel); buttons.Children.Add(apply); content.Children.Add(buttons); dialog.Content = content;
        cancel.Click += (_, _) => dialog.Close(null); apply.Click += (_, _) => { if (vm.IsValid) dialog.Close(new DocumentSettings((float)vm.Width, (float)vm.Height, vm.BackgroundColor, vm.Title)); };
        return await dialog.ShowDialog<DocumentSettings?>(owner);
    }

    public async Task<bool> ShowRotateLayerDialogAsync(RotateLayerViewModel vm)
    {
        var dialog = new Window
        {
            Title = "レイヤーの角度指定回転",
            Width = 420,
            Height = 290,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            DataContext = vm
        };

        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        content.Children.Add(new TextBlock { Text = "回転角度 (-360° 〜 +360°)" });

        var angleRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,110") };
        var slider = new Slider { Minimum = -360, Maximum = 360, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
        slider.Bind(Slider.ValueProperty, new Binding(nameof(vm.Degrees)) { Mode = BindingMode.TwoWay });
        var numeric = new NumericUpDown { Minimum = -360, Maximum = 360, Increment = 5 };
        numeric.Bind(NumericUpDown.ValueProperty, new Binding(nameof(vm.Degrees)) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(numeric, 1);
        angleRow.Children.Add(slider);
        angleRow.Children.Add(numeric);
        content.Children.Add(angleRow);

        content.Children.Add(new TextBlock { Text = "回転中心" });
        var centerCombo = new ComboBox { ItemsSource = vm.CenterOptions, HorizontalAlignment = HorizontalAlignment.Stretch };
        centerCombo.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(vm.SelectedCenterOption)) { Mode = BindingMode.TwoWay });
        content.Children.Add(centerCombo);

        var liveCheck = new CheckBox { Content = "リアルタイムプレビュー" };
        liveCheck.Bind(CheckBox.IsCheckedProperty, new Binding(nameof(vm.LivePreview)) { Mode = BindingMode.TwoWay });
        content.Children.Add(liveCheck);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "キャンセル" };
        var apply = new Button { Content = "回転を適用" };
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        content.Children.Add(buttons);
        dialog.Content = content;

        cancel.Click += (_, _) => dialog.Close(false);
        apply.Click += (_, _) => dialog.Close(true);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task ShowErrorAsync(string message)
    {
        var dialog = new Window { Title = "ファイル操作エラー", Width = 520, Height = 210, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 16 }; var close = new Button { Content = "閉じる", HorizontalAlignment = HorizontalAlignment.Right };
        content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); content.Children.Add(close); dialog.Content = content;
        close.Click += (_, _) => dialog.Close(); await dialog.ShowDialog(owner);
    }
}
