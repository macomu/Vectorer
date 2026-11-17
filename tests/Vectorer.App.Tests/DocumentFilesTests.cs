using System.Text;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class DocumentFilesTests
{
    private sealed class Dialogs : IEditorFileDialogs
    {
        public string Input { get; set; } = "<svg xmlns='http://www.w3.org/2000/svg' width='400' height='300'><rect width='50' height='60'/></svg>";
        public MemoryStream Output { get; private set; } = new();
        public bool Discard { get; set; }
        public bool SaveToSource { get; set; }
        public LocalizedMessage? LastError { get; private set; }
        public Task ShowLocalizedErrorAsync(LocalizedMessage message, string fallback)
        {
            LastError = message;
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(Discard);
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(new("input.svg", () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Input))), () => throw new NotSupportedException()));
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(new(SaveToSource ? "input.svg" : "output.svg", () => throw new NotSupportedException(), () => { Output = new MemoryStream(); return Task.FromResult<Stream>(Output); }));
    }
    [Fact]
    public async Task Save_TracksCleanStateAcrossUndoAndRedo()
    {
        var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer!, new RectangleShapeNode())); Assert.True(vm.HasUnsavedChanges);
        await vm.SaveDocumentCommand.ExecuteAsync(null);
        Assert.False(vm.HasUnsavedChanges); Assert.Contains("<svg", Encoding.UTF8.GetString(dialogs.Output.ToArray()));
        vm.UndoCommand.Execute(null); Assert.True(vm.HasUnsavedChanges);
        vm.RedoCommand.Execute(null); Assert.False(vm.HasUnsavedChanges);
    }
    [Fact]
    public async Task Open_LossyImportForcesSeparateDestinationEvenWhenOriginalIsSelected()
    {
        var dialogs = new Dialogs
        {
            Input = "<svg xmlns='http://www.w3.org/2000/svg'><rect width='10' height='10'/><line x2='10'/></svg>",
            SaveToSource = true
        };
        var vm = new MainWindowViewModel { FileDialogs = dialogs };
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        Assert.Single(vm.ActiveLayer!.Children);

        vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer, new RectangleShapeNode()));
        await vm.SaveDocumentCommand.ExecuteAsync(null);
        Assert.True(vm.HasUnsavedChanges);
        Assert.Empty(dialogs.Output.ToArray());

        dialogs.SaveToSource = false;
        await vm.SaveDocumentCommand.ExecuteAsync(null);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Contains("<rect", Encoding.UTF8.GetString(dialogs.Output.ToArray()));
    }

    [Fact]
    public async Task AtomicSave_PreservesOriginalAndRemovesTemporaryFileOnWriteFailure()
    {
        var directory = Directory.CreateTempSubdirectory("Vectorer-save-test-");
        var path = Path.Combine(directory.FullName, "original.svg");
        var newPath = Path.Combine(directory.FullName, "new.svg");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            await Assert.ThrowsAsync<IOException>(() => AtomicFileSave.WriteAsync(path, async stream =>
            {
                await stream.WriteAsync("partial"u8.ToArray());
                throw new IOException("Simulated write failure");
            }));
            Assert.Equal("original", await File.ReadAllTextAsync(path));
            Assert.Single(directory.GetFiles());

            await AtomicFileSave.WriteAsync(path, stream => stream.WriteAsync("replacement"u8.ToArray()).AsTask());
            Assert.Equal("replacement", await File.ReadAllTextAsync(path));
            Assert.Single(directory.GetFiles());

            await AtomicFileSave.WriteAsync(newPath, stream => stream.WriteAsync("new document"u8.ToArray()).AsTask());
            Assert.Equal("new document", await File.ReadAllTextAsync(newPath));
        }
        finally
        {
            File.Delete(path);
            File.Delete(newPath);
            directory.Delete();
        }
    }

    [Fact]
    public async Task Open_InvalidSvgPreservesCurrentDocumentAndResetsBusyState()
    {
        var dialogs = new Dialogs { Input = "<broken" }; var vm = new MainWindowViewModel { FileDialogs = dialogs };
        var before = vm.CurrentDocument;
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        Assert.Same(before, vm.CurrentDocument);
        Assert.False(vm.IsBusy);
        Assert.Equal(nameof(Strings.Status_FileOpenFailed), dialogs.LastError?.Key);
    }
    [Fact]
    public async Task NewDocument_RespectsDiscardDecisionAndClearsHistoryAfterAcceptance()
    {
        var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        var before = vm.CurrentDocument; vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer!, new RectangleShapeNode()));
        await vm.NewDocumentCommand.ExecuteAsync(null); Assert.Same(before, vm.CurrentDocument);
        dialogs.Discard = true;
        await vm.NewDocumentCommand.ExecuteAsync(null);
        Assert.NotSame(before, vm.CurrentDocument); Assert.False(vm.HasUnsavedChanges);
        Assert.Empty(vm.ActiveLayer!.Children); vm.UndoCommand.Execute(null); Assert.Empty(vm.ActiveLayer.Children);
    }
}
