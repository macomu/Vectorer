using System.Text;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.Core.Commands;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class RecentFilesTests
{
    private sealed class Store : IEditorUserSettingsStore
    {
        public EditorUserSettings Settings { get; set; } = new();
        public Task<EditorUserSettings> LoadAsync() => Task.FromResult(Settings);
        public Task SaveAsync(EditorUserSettings settings) { Settings = settings; return Task.CompletedTask; }
    }
    private sealed class Dialogs : IEditorFileDialogs
    {
        public Uri Location { get; set; } = new("file:///C:/drawings/first.svg");
        public string Svg { get; set; } = "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'/>";
        public bool Missing { get; set; }
        public bool Discard { get; set; }
        public bool FailWrite { get; set; }
        public int ResolveCount { get; private set; }
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(Discard);
        private EditorFile File() => new(Path.GetFileName(Location.LocalPath),
            () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Svg))),
            () => FailWrite ? throw new IOException("Write failed") : Task.FromResult<Stream>(new MemoryStream()), Location);
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(File());
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(File());
        public Task<EditorFile?> ResolveSvgAsync(Uri uri)
        { ResolveCount++; Location = uri; return Task.FromResult<EditorFile?>(Missing ? null : File()); }
    }

    [Fact]
    public async Task History_PersistsTenUniqueEntriesAndMovesOpenedFilesToFront()
    {
        var store = new Store(); var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        await vm.LoadUserSettingsAsync(store);
        for (var i = 0; i < 12; i++)
        {
            dialogs.Location = new($"file:///C:/drawings/{i}.svg");
            await vm.OpenDocumentCommand.ExecuteAsync(null);
        }
        Assert.Equal(10, vm.RecentFiles.Count);
        var recent = vm.RecentFiles[5];
        await vm.OpenRecentFileCommand.ExecuteAsync(recent);
        Assert.Equal(recent, vm.RecentFiles[0]); Assert.Equal(10, vm.RecentFiles.Count);
        await vm.SaveUserSettingsAsync(480);
        var restored = new MainWindowViewModel(); await restored.LoadUserSettingsAsync(store);
        Assert.Equal(480, restored.InspectorWidth); Assert.Equal(recent, restored.RecentFiles[0]);
        await restored.ClearRecentFilesCommand.ExecuteAsync(null);
        Assert.Empty(store.Settings.RecentFiles!); Assert.False(restored.HasRecentFiles);
    }

    [Fact]
    public async Task MissingOrInvalidRecentFile_PreservesCurrentDocumentAndHistory()
    {
        var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        var before = vm.CurrentDocument; var recent = vm.RecentFiles[0];
        dialogs.Missing = true; await vm.OpenRecentFileCommand.ExecuteAsync(recent);
        Assert.Same(before, vm.CurrentDocument); Assert.False(vm.IsBusy); Assert.Single(vm.RecentFiles);
        dialogs.Missing = false; dialogs.Svg = "<broken";
        await vm.OpenRecentFileCommand.ExecuteAsync(recent);
        Assert.Same(before, vm.CurrentDocument); Assert.False(vm.IsBusy); Assert.Single(vm.RecentFiles);
    }

    [Fact]
    public async Task CancelUnsavedProtectionAndFailedSave_DoNotUpdateHistory()
    {
        var dialogs = new Dialogs(); var vm = new MainWindowViewModel { FileDialogs = dialogs };
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        vm.ExecuteCommand(new AddNodeCommand(vm.ActiveLayer!, new RectangleShapeNode()));
        await vm.OpenRecentFileCommand.ExecuteAsync(vm.RecentFiles[0]);
        Assert.Equal(0, dialogs.ResolveCount); Assert.True(vm.HasUnsavedChanges);
        dialogs.Location = new("file:///C:/drawings/failed.svg"); dialogs.FailWrite = true;
        await vm.SaveDocumentAsCommand.ExecuteAsync(null);
        Assert.Single(vm.RecentFiles); Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task Settings_ValidateStoredWidthAndRecoverFromCorruptJson()
    {
        var store = new Store { Settings = new(double.PositiveInfinity, [new("bad", "relative")]) };
        var vm = new MainWindowViewModel(); await vm.LoadUserSettingsAsync(store);
        Assert.Equal(300, vm.InspectorWidth); Assert.Empty(vm.RecentFiles);
        var path = Path.Combine(Path.GetTempPath(), $"vectorer-settings-{Guid.NewGuid():N}.json");
        try
        {
            var json = new JsonEditorUserSettingsStore(path);
            await json.SaveAsync(new(420, [new("one.svg", "file:///C:/one.svg")]));
            Assert.Equal(420, (await json.LoadAsync()).InspectorWidth);
            await File.WriteAllTextAsync(path, "invalid json");
            Assert.Equal(300, (await json.LoadAsync()).InspectorWidth);
        }
        finally { File.Delete(path); }
    }
}
