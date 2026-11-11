using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Services;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IEditorFileDialogs? FileDialogs { get; set; }
    [ObservableProperty] private bool _isBusy;
    private EditorFile? _currentFile;
    private IDocumentCommand? _savedCommand;
    private bool _untrackedChanges;
    public bool HasUnsavedChanges => _untrackedChanges || _undoRedoService.UndoStack.LastOrDefault() != _savedCommand;
    public string WindowTitle => $"{CurrentDocument.Title}{(HasUnsavedChanges ? " *" : "")} — Vectorer";
    private void NotifyDocumentState() => OnPropertyChanged(nameof(WindowTitle));
    public async Task<bool> CanCloseAsync()
    {
        FinishTextEditing();
        return !IsBusy && (!HasUnsavedChanges || FileDialogs is not null && await FileDialogs.ConfirmDiscardAsync());
    }

    [RelayCommand] private async Task NewDocumentAsync()
    {
        if (IsBusy || IsInteracting) return;
        FinishTextEditing();
        if (HasUnsavedChanges && (FileDialogs is null || !await FileDialogs.ConfirmDiscardAsync())) return;
        ReplaceDocument(SvgDocument.CreateDefault(860, 540), null);
    }
    [RelayCommand] private async Task OpenDocumentAsync()
    {
        if (IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        if (HasUnsavedChanges && !await FileDialogs.ConfirmDiscardAsync()) return;
        IsBusy = true;
        try
        {
            var file = await FileDialogs.OpenSvgAsync(); if (file is null) return;
            await using var stream = await file.OpenRead();
            var document = await Task.Run(() => _svgSerializer.LoadFromStream(stream));
            ReplaceDocument(document, file);
            StatusMessage = $"開きました: {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or ArgumentException)
        { StatusMessage = $"開けませんでした: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    [RelayCommand] private Task SaveDocumentAsync() => SaveDocumentCoreAsync(false);
    [RelayCommand] private Task SaveDocumentAsAsync() => SaveDocumentCoreAsync(true);
    private async Task SaveDocumentCoreAsync(bool saveAs)
    {
        if (IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        IsBusy = true;
        try
        {
            var file = saveAs || _currentFile is null ? await FileDialogs.SaveSvgAsync(_currentFile?.Name ?? "drawing.svg") : _currentFile;
            if (file is null) return;
            var bytes = await Task.Run(() => System.Text.Encoding.UTF8.GetBytes(_svgSerializer.SerializeToString(CurrentDocument)));
            await using var stream = await file.OpenWrite();
            if (stream.CanSeek) stream.SetLength(0);
            await stream.WriteAsync(bytes);
            _currentFile = file; _savedCommand = _undoRedoService.UndoStack.LastOrDefault(); _untrackedChanges = false;
            NotifyDocumentState(); StatusMessage = $"保存しました: {file.Name}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { StatusMessage = $"保存できませんでした: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private void ReplaceDocument(SvgDocument document, EditorFile? file)
    {
        CancelInteraction(); _scope.Clear(); CurrentDocument = document; _currentFile = file;
        _undoRedoService.Clear(); _savedCommand = null; _untrackedChanges = false;
        ActiveLayer = null; SyncLayersCollection(); SelectNodes([]); NotifyScope(); NotifyDocumentState();
    }
}
