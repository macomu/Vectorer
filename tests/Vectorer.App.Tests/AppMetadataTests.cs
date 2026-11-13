using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class AppMetadataTests
{
    private sealed class StubAboutDialogs : IEditorFileDialogs
    {
        public AppMetadata? ReceivedMetadata { get; private set; }

        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(null);
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(null);
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);

        public Task ShowAboutDialogAsync(AppMetadata metadata)
        {
            ReceivedMetadata = metadata;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void CurrentMetadata_ReadsVersionProductAndCopyrightFromAssembly()
    {
        var metadata = AppMetadata.Current;

        Assert.Equal("Vectorer", metadata.ProductName);
        Assert.Equal("0.1.0", metadata.Version);
        Assert.Equal("MIT License", metadata.License);
        Assert.Equal("Copyright (c) 2026 Vectorer Contributors", metadata.Copyright);
        Assert.False(string.IsNullOrWhiteSpace(metadata.RuntimeSummary));
    }

    [Fact]
    public async Task OpenAboutDialogCommand_PassesCurrentMetadataToDialogs()
    {
        var dialogs = new StubAboutDialogs();
        var vm = new MainWindowViewModel
        {
            FileDialogs = dialogs
        };

        await vm.OpenAboutDialogCommand.ExecuteAsync(null);

        Assert.NotNull(dialogs.ReceivedMetadata);
        Assert.Equal("Vectorer", dialogs.ReceivedMetadata!.ProductName);
        Assert.Equal("0.1.0", dialogs.ReceivedMetadata.Version);
    }
}
