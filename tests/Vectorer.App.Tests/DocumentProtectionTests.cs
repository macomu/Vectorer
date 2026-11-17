using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class DocumentProtectionTests
{
    private sealed class Dialogs : IEditorFileDialogs
    {
        public UnsavedDecision Decision { get; set; } = UnsavedDecision.Save;
        public bool CancelSave { get; set; }
        public bool FailFlush { get; set; }
        public DocumentSettings? Settings { get; set; } = new(320, 240, SKColors.White, "Changed");
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(false);
        public Task<UnsavedDecision> ConfirmUnsavedAsync() => Task.FromResult(Decision);
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(null);
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult(CancelSave ? null : new EditorFile("test.svg", () => throw new NotSupportedException(), () => Task.FromResult<Stream>(FailFlush ? new FailingStream() : new MemoryStream())));
        public Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew) => Task.FromResult(Settings);
    }
    private sealed class FailingStream : MemoryStream
    { public override Task FlushAsync(CancellationToken cancellationToken) => Task.FromException(new IOException("Disk flush failed")); }
    [Fact]
    public async Task SaveFailureDuringFlush_KeepsDocumentDirtyAndBlocksSaveAndClose()
    {
        var dialogs = new Dialogs { FailFlush = true }; var vm = new MainWindowViewModel { FileDialogs = dialogs };
        vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer!, new RectangleShapeNode()));
        await vm.SaveDocumentCommand.ExecuteAsync(null); Assert.True(vm.HasUnsavedChanges); Assert.False(vm.IsBusy);
        Assert.False(await vm.CanCloseAsync());
        Assert.Equal(vm.L10n.Format(new LocalizedMessage(nameof(Strings.Status_FileSaveFailed), "Disk flush failed")), vm.StatusMessage);
    }
    [Fact]
    public async Task Close_SaveChoiceBlocksClosingWhenSavePickerIsCancelled()
    {
        var dialogs = new Dialogs { CancelSave = true }; var vm = new MainWindowViewModel { FileDialogs = dialogs };
        vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer!, new RectangleShapeNode()));
        Assert.False(await vm.CanCloseAsync()); Assert.True(vm.HasUnsavedChanges);
        dialogs.CancelSave = false; Assert.True(await vm.CanCloseAsync()); Assert.False(vm.HasUnsavedChanges);
    }
    [Fact]
    public async Task DocumentProperties_AreUndoableAndCancellationKeepsTheDocument()
    {
        var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        var before = vm.CurrentDocument.Width; var box = vm.CurrentDocument.ViewBox;
        await vm.DocumentPropertiesCommand.ExecuteAsync(null);
        Assert.Equal(320, vm.CurrentDocument.Width); Assert.Equal(new SKRect(0, 0, 320, 240), vm.CurrentDocument.ViewBox); Assert.True(vm.HasUnsavedChanges);
        vm.UndoCommand.Execute(null); Assert.Equal(before, vm.CurrentDocument.Width); Assert.Equal(box, vm.CurrentDocument.ViewBox);
        dialogs.Settings = null; await vm.DocumentPropertiesCommand.ExecuteAsync(null); Assert.Equal(before, vm.CurrentDocument.Width);
    }
    [Fact]
    public void DocumentSettings_PresetsSwapAndValidation()
    {
        var vm = new DocumentSettingsViewModel(); vm.ApplyPreset(vm.Presets.First(p => p.Name == "FHD"));
        Assert.Equal(1920, vm.Width); Assert.Equal(1080, vm.Height); vm.SwapDimensionsCommand.Execute(null); Assert.Equal(1080, vm.Width);
        vm.Background = "invalid"; Assert.False(vm.IsValid); vm.Transparent = true; Assert.True(vm.IsValid);
        vm.Width = 0; Assert.False(vm.IsValid);
    }
}
