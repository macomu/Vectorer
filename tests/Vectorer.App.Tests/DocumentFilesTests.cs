using System.Text;
using Vectorer.Abstractions.Documents;
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
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(Discard);
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(new("input.svg", () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Input))), () => throw new NotSupportedException()));
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(new("output.svg", () => throw new NotSupportedException(), () => { Output = new MemoryStream(); return Task.FromResult<Stream>(Output); }));
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
    public async Task Open_InvalidSvgPreservesCurrentDocumentAndResetsBusyState()
    {
        var dialogs = new Dialogs { Input = "<broken" }; var vm = new MainWindowViewModel { FileDialogs = dialogs };
        var before = vm.CurrentDocument;
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        Assert.Same(before, vm.CurrentDocument); Assert.False(vm.IsBusy); Assert.Contains("開けませんでした", vm.StatusMessage);
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
