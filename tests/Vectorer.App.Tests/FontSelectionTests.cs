using Vectorer.Abstractions.Documents;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class FontSelectionTests
{
    private sealed class Catalog : IInstalledFontCatalog
    {
        public Task<IReadOnlyList<string>> GetFamiliesAsync() => Task.FromResult<IReadOnlyList<string>>(["Arial", "sans-serif"]);
    }

    [Fact]
    public async Task LoadingFontChoices_PreservesMissingDocumentFontAndEditsAreUndoable()
    {
        var text = new TextShapeNode { Text = "Vectorer", FontFamily = "MissingDocumentFont" };
        var vm = new MainWindowViewModel(); vm.ActiveLayer!.Children.Add(text);
        vm.SelectNodes([text]); vm.ChooseTool(DrawingTool.Text);
        await vm.LoadInstalledFontsAsync(new Catalog());
        Assert.Equal("MissingDocumentFont", vm.FontFamily); Assert.False(vm.HasUnsavedChanges);
        Assert.Contains("Arial", vm.InstalledFontFamilies);
        vm.FontFamily = "Arial"; Assert.Equal("Arial", text.FontFamily);
        vm.UndoCommand.Execute(null); Assert.Equal("MissingDocumentFont", text.FontFamily);
        vm.SelectNodes([text]); Assert.Equal("MissingDocumentFont", vm.FontFamily);
        vm.RedoCommand.Execute(null); Assert.Equal("Arial", text.FontFamily);
        var svg = new Vectorer.Svg.SvgSerializer().SerializeToString(vm.CurrentDocument);
        Assert.Contains("Arial", svg);
    }

    [Fact]
    public async Task FontCatalog_CachesAvailableFamiliesWithoutRequiringDocumentChanges()
    {
        var catalog = new InstalledFontCatalog(); var first = await catalog.GetFamiliesAsync();
        Assert.Same(first, await new InstalledFontCatalog().GetFamiliesAsync());
        Assert.Contains("sans-serif", first); Assert.Contains("serif", first); Assert.Contains("monospace", first);
        Assert.Equal(first.Count, first.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
