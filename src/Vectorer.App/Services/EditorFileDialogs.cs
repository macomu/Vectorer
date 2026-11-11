using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Vectorer.App.Services;

public sealed record EditorFile(string Name, Func<Task<Stream>> OpenRead, Func<Task<Stream>> OpenWrite);

public interface IEditorFileDialogs
{
    Task<EditorFile?> OpenSvgAsync();
    Task<EditorFile?> SaveSvgAsync(string suggestedName);
    Task<bool> ConfirmDiscardAsync();
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
    public async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new Window { Title = "未保存の変更", Width = 380, Height = 160, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var discard = new Button { Content = "変更を破棄して続行" }; var cancel = new Button { Content = "キャンセル" };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(discard);
        var content = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 20 };
        content.Children.Add(new TextBlock { Text = "未保存の変更があります。続行しますか？" }); content.Children.Add(buttons);
        dialog.Content = content;
        discard.Click += (_, _) => dialog.Close(true); cancel.Click += (_, _) => dialog.Close(false);
        return await dialog.ShowDialog<bool>(owner);
    }
}
