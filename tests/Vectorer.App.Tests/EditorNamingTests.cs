using System.Globalization;
using Avalonia.Threading;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Vectorer.Svg;
using Xunit;

namespace Vectorer.App.Tests;

[Collection("Localization")]
public sealed class EditorNamingTests
{
    private sealed class StubDialogs(DocumentSettings? nextDocSettings = null) : IEditorFileDialogs
    {
        public DocumentSettings? LastInitialSettings { get; private set; }
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(null);
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(null);
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);
        public Task<DocumentSettings?> EditDocumentAsync(DocumentSettings current, bool isNew)
        {
            LastInitialSettings = current;
            return Task.FromResult(nextDocSettings);
        }
    }

    [Fact]
    public void GeneratedNames_UseCreationLanguage_AndDoNotChangeWhenLanguageSwitches()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("de-DE"), "ja");
        var vm = new MainWindowViewModel(l10n);

        Assert.Equal("無題", vm.CurrentDocument.Title);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);

        vm.NewLayerCommand.Execute(null);
        Assert.Equal("レイヤー 2", vm.ActiveLayer!.Name);

        vm.DuplicateLayerCommand.Execute(null);
        Assert.Equal("レイヤー 2 のコピー", vm.ActiveLayer!.Name);

        vm.ChooseTool(DrawingTool.Rectangle);
        vm.BeginInteraction(new SKPoint(10, 10), shift: false);
        vm.UpdateInteraction(new SKPoint(90, 70), shift: false, alt: false);
        vm.EndInteraction();
        Assert.Equal("四角形", vm.ActiveLayer!.Children[^1].Name);

        // Switch UI language to English -> existing names must remain in Japanese
        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("無題", vm.CurrentDocument.Title);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);
        Assert.Equal("レイヤー 2", vm.CurrentDocument.Layers[1].Name);
        Assert.Equal("レイヤー 2 のコピー", vm.CurrentDocument.Layers[2].Name);
        Assert.Equal("四角形", vm.ActiveLayer!.Children[^1].Name);

        // New items created in English get English names
        vm.NewLayerCommand.Execute(null);
        Assert.Equal("Layer 4", vm.ActiveLayer!.Name);
        vm.DuplicateLayerCommand.Execute(null);
        Assert.Equal("Layer 4 copy", vm.ActiveLayer!.Name);
        vm.AddCircleCommand.Execute(null);
        Assert.Equal("Circle", vm.ActiveLayer!.Children[^1].Name);
    }

    [Fact]
    public async Task NewDocument_RespectsCancelAndUserTitle_AndUndoRedoPreservesCreationNameAcrossSwitch()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.GetCultureInfo("de-DE"), "ja");
        var vm = new MainWindowViewModel(l10n);

        // Cancel NewDocument -> keeps existing document
        var originalDoc = vm.CurrentDocument;
        vm.FileDialogs = new StubDialogs(null);
        await vm.NewDocumentCommand.ExecuteAsync(null);
        Assert.Same(originalDoc, vm.CurrentDocument);

        // Accept NewDocument with explicit "Untitled" title -> preserved even in Japanese UI
        var dialogs = new StubDialogs(new DocumentSettings(1000, 800, SKColors.White, "Untitled"));
        vm.FileDialogs = dialogs;
        await vm.NewDocumentCommand.ExecuteAsync(null);

        Assert.Equal("無題", dialogs.LastInitialSettings!.Title);
        Assert.Equal("Untitled", vm.CurrentDocument.Title);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);

        // Delete the only layer in Japanese -> creates replacement "レイヤー 1"
        vm.DeleteLayerCommand.Execute(null);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);

        // Switch to English, Undo, Redo -> replacement layer keeps "レイヤー 1"
        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();

        vm.UndoCommand.Execute(null);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);
        vm.RedoCommand.Execute(null);
        Assert.Equal("レイヤー 1", vm.CurrentDocument.Layers[0].Name);

        // SVG round-trip under de-DE formatting culture preserves names and invariant numbers
        var serializer = new SvgSerializer();
        using var ms = new MemoryStream();
        serializer.SaveToStream(vm.CurrentDocument, ms);
        ms.Position = 0;
        var svgText = new StreamReader(ms).ReadToEnd();
        Assert.Contains("width=\"1000\"", svgText);
        ms.Position = 0;
        var reloaded = serializer.LoadFromStream(ms);
        Assert.Equal("Untitled", reloaded.Title);
        Assert.Equal("レイヤー 1", reloaded.Layers[0].Name);
    }
}
