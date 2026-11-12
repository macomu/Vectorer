using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
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
        return !IsBusy && await ProtectUnsavedAsync();
    }

    [RelayCommand] private async Task NewDocumentAsync()
    {
        if (IsBusy || IsInteracting) return;
        FinishTextEditing();
        if (!await ProtectUnsavedAsync()) return;
        var settings = FileDialogs is not null ? await FileDialogs.EditDocumentAsync(new DocumentSettings(860, 540, SkiaSharp.SKColors.Transparent, "Untitled"), true)
            : new DocumentSettings(860, 540, SkiaSharp.SKColors.Transparent, "Untitled");
        if (settings is null) return;
        var document = SvgDocument.CreateDefault(settings.Width, settings.Height); document.Title = settings.Title; document.BackgroundColor = settings.Background;
        ReplaceDocument(document, null);
    }
    [RelayCommand] private async Task OpenDocumentAsync()
    {
        if (IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        if (!await ProtectUnsavedAsync()) return;
        IsBusy = true;
        try
        {
            var file = await FileDialogs.OpenSvgAsync(); if (file is null) return;
            await OpenFileCoreAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or ArgumentException)
        { SetStatus(nameof(Strings.Status_FileOpenFailed), ex.Message); await FileDialogs.ShowLocalizedErrorAsync(_status, StatusMessage); }
        finally { IsBusy = false; }
    }
    private async Task OpenFileCoreAsync(EditorFile file)
    {
        await using var stream = await file.OpenRead();
        var document = await Task.Run(() => _svgSerializer.LoadFromStream(stream));
        ReplaceDocument(document, file);
        SetStatus(nameof(Strings.Status_FileOpened), file.Name);
        await RememberFileAsync(file);
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
            await using (var stream = await file.OpenWrite())
            {
                if (stream.CanSeek) stream.SetLength(0);
                await stream.WriteAsync(bytes);
                await stream.FlushAsync();
            }
            _currentFile = file; _savedCommand = _undoRedoService.UndoStack.LastOrDefault(); _untrackedChanges = false;
            NotifyDocumentState(); SetStatus(nameof(Strings.Status_FileSaved), file.Name);
            await RememberFileAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        { SetStatus(nameof(Strings.Status_FileSaveFailed), ex.Message); await FileDialogs.ShowLocalizedErrorAsync(_status, StatusMessage); }
        finally { IsBusy = false; }
    }
    private async Task<bool> ProtectUnsavedAsync()
    {
        if (!HasUnsavedChanges) return true;
        if (FileDialogs is null) return false;
        var decision = await FileDialogs.ConfirmUnsavedAsync();
        if (decision == UnsavedDecision.Discard) return true;
        if (decision != UnsavedDecision.Save) return false;
        await SaveDocumentCoreAsync(false);
        return !HasUnsavedChanges;
    }
    [RelayCommand] private async Task DocumentPropertiesAsync()
    {
        if (IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        var document = CurrentDocument;
        var value = await FileDialogs.EditDocumentAsync(new DocumentSettings(document.Width, document.Height, document.BackgroundColor, document.Title), false);
        if (value is null) return;
        var before = (document.Width, document.Height, document.BackgroundColor, document.Title, document.ViewBox);
        ExecuteCommand(new Vectorer.Core.Commands.DelegateDocumentCommand("Document properties", () =>
        { document.Width = value.Width; document.Height = value.Height; document.BackgroundColor = value.Background; document.Title = value.Title; document.ViewBox = new SkiaSharp.SKRect(0, 0, value.Width, value.Height); },
            () => { document.Width = before.Width; document.Height = before.Height; document.BackgroundColor = before.BackgroundColor; document.Title = before.Title; document.ViewBox = before.ViewBox; }));
        NotifyDocumentState();
    }
    public event Action? CloseRequested;
    [RelayCommand] private void ExitApplication() => CloseRequested?.Invoke();

    public PngExportViewModel CreatePngExportViewModel() =>
        new(CurrentDocument, SelectedNodes, (Vectorer.Rendering.Export.PngExportService)_pngExportService, L10n);

    [RelayCommand]
    private async Task ExportPngAsync()
    {
        if (IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        using var exportVm = CreatePngExportViewModel();
        if (!await FileDialogs.ShowPngExportDialogAsync(exportVm) || !exportVm.CanExport)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (exportVm.Mode == PngExportMode.LayersBatch)
            {
                var folder = await FileDialogs.SelectExportFolderAsync();
                if (string.IsNullOrWhiteSpace(folder)) return;
                var options = exportVm.CreateExportOptions();
                var prefix = exportVm.BatchFilePrefix.Trim();
                var visibleOnly = exportVm.VisibleLayersOnly;
                var files = await Task.Run(() => _pngExportService.ExportLayersBatchAsync(CurrentDocument, folder, prefix, visibleOnly, options));
                SetStatus(nameof(Strings.Status_PngBatchExported), files.Count, folder);
            }
            else
            {
                var suggested = string.IsNullOrWhiteSpace(CurrentDocument.Title) ? "export.png" : $"{CurrentDocument.Title}.png";
                var file = await FileDialogs.SavePngAsync(suggested);
                if (file is null) return;
                var bytes = await Task.Run(() => exportVm.RenderExportPngBytes());
                await using (var stream = await file.OpenWrite())
                {
                    if (stream.CanSeek) stream.SetLength(0);
                    await stream.WriteAsync(bytes);
                    await stream.FlushAsync();
                }
                SetStatus(nameof(Strings.Status_PngExported), file.Name, exportVm.TargetWidth, exportVm.TargetHeight);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus(nameof(Strings.Status_PngExportFailed), ex.Message);
            await FileDialogs.ShowLocalizedErrorAsync(_status, StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }
    private void ReplaceDocument(SvgDocument document, EditorFile? file)
    {
        CancelInteraction(); _scope.Clear(); CurrentDocument = document; _currentFile = file;
        _undoRedoService.Clear(); _savedCommand = null; _untrackedChanges = false;
        ActiveLayer = null; SyncLayersCollection(); SelectNodes([]); NotifyScope(); NotifyDocumentState();
    }
}
