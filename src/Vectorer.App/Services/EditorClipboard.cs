using Avalonia.Controls;

namespace Vectorer.App.Services;

public interface IEditorClipboard
{
    Task<string?> ReadAsync();
    Task WriteAsync(string text);
}

public sealed class MemoryEditorClipboard : IEditorClipboard
{
    private string? _text;
    public Task<string?> ReadAsync() => Task.FromResult(_text);
    public Task WriteAsync(string text) { _text = text; return Task.CompletedTask; }
}

public sealed class EditorClipboard(Window owner) : IEditorClipboard
{
    public Task<string?> ReadAsync() => owner.Clipboard?.GetTextAsync() ?? Task.FromResult<string?>(null);
    public Task WriteAsync(string text) => owner.Clipboard is { } clipboard ? clipboard.SetTextAsync(text) : throw new InvalidOperationException("Clipboard is unavailable.");
}
