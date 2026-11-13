using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia.Threading;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

[Collection("Localization")]
public sealed partial class LocalizationTests : IDisposable
{
    private readonly CultureInfo _originalUiCulture = CultureInfo.CurrentUICulture;
    private readonly CultureInfo? _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
    }
    [GeneratedRegex(@"\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}")]
    private static partial Regex FormatPlaceholderRegex();

    [Fact]
    public void ResxFiles_HaveIdenticalKeys_NoEmptyValues_AndMatchingFormatArguments()
    {
        var localizationDir = FindLocalizationDirectory();
        var baseResx = Path.Combine(localizationDir, "Strings.resx");
        var jaResx = Path.Combine(localizationDir, "Strings.ja.resx");

        Assert.True(File.Exists(baseResx), $"Missing {baseResx}");
        Assert.True(File.Exists(jaResx), $"Missing {jaResx}");

        var baseEntries = LoadResxEntries(baseResx);
        var jaEntries = LoadResxEntries(jaResx);

        Assert.Equal(baseEntries.Keys.OrderBy(k => k, StringComparer.Ordinal), jaEntries.Keys.OrderBy(k => k, StringComparer.Ordinal));

        foreach (var (key, baseValue) in baseEntries)
        {
            Assert.False(string.IsNullOrWhiteSpace(baseValue), $"Base resx key '{key}' has empty value.");
            var jaValue = jaEntries[key];
            Assert.False(string.IsNullOrWhiteSpace(jaValue), $"Japanese resx key '{key}' has empty value.");

            var baseArgs = ExtractFormatIndices(baseValue);
            var jaArgs = ExtractFormatIndices(jaValue);
            Assert.Equal(baseArgs, jaArgs);
        }
    }

    [Theory]
    [InlineData("ja", "ja-JP", "ja")]
    [InlineData("en", "ja-JP", "en")]
    [InlineData("system", "ja-JP", "ja")]
    [InlineData("system", "ja", "ja")]
    [InlineData("system", "en-US", "en")]
    [InlineData("system", "en-GB", "en")]
    [InlineData("system", "fr-FR", "en")]
    [InlineData("unknown", "ja-JP", "ja")]
    [InlineData("", "en-US", "en")]
    [InlineData(null, "ja-JP", "ja")]
    public void ResolveCulture_HandlesExplicitSystemAndFallbackValues(string? requested, string osUiCultureName, string expectedCultureName)
    {
        var osCulture = CultureInfo.GetCultureInfo(osUiCultureName);
        var resolved = AppLocalization.ResolveCulture(requested, osCulture);
        Assert.Equal(expectedCultureName, resolved.Name);
    }

    [Fact]
    public async Task LanguageChangeCoordinator_SwitchesImmediateAndRestoresSystemLanguage()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vectorer-l10n-{Guid.NewGuid():N}.json");
        try
        {
            var store = new JsonEditorUserSettingsStore(tempFile);
            var osCulture = CultureInfo.GetCultureInfo("ja-JP");
            var formattingCulture = CultureInfo.GetCultureInfo("de-DE");
            var l10n = new AppLocalization(osCulture, formattingCulture, "system");
            var vm = new MainWindowViewModel(l10n);
            vm.ApplyLoadedSettings(new EditorUserSettings(340, null, "system"), store);

            Assert.Equal("ja", l10n.UiCulture.Name);
            Assert.Equal("ファイル(_F)", l10n.Menu_File);

            vm.SetLanguageCommand.Execute("en");
            Dispatcher.UIThread.RunJobs();
            await vm.LanguageCoordinator.WaitForPendingSavesAsync();
            Assert.Equal("en", l10n.UiCulture.Name);
            Assert.Equal("_File", l10n.Menu_File);
            Assert.True(l10n.IsEnglishLanguage);

            vm.SetLanguageCommand.Execute("system");
            Dispatcher.UIThread.RunJobs();
            await vm.LanguageCoordinator.WaitForPendingSavesAsync();
            Assert.Equal("ja", l10n.UiCulture.Name);
            Assert.Equal("ファイル(_F)", l10n.Menu_File);
            Assert.True(l10n.IsSystemLanguage);

            // Saving inspector width preserves UiLanguage
            await vm.SaveUserSettingsAsync(380);
            var reloaded = await store.LoadAsync();
            Assert.Equal(380, reloaded.InspectorWidth);
            Assert.Equal("system", reloaded.UiLanguage);
            Assert.Equal("de-DE", l10n.FormattingCulture.Name);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LanguageChangeCoordinator_DefersWhenUnsafeAndAppliesLastRequestedValue()
    {
        var canApply = false;
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var coordinator = new LanguageChangeCoordinator(l10n, canApplyNow: () => canApply);

        await coordinator.RequestLanguageAsync("en");
        await coordinator.RequestLanguageAsync("system");

        Assert.Equal("ja", l10n.RequestedLanguage);
        Assert.Equal("system", coordinator.PendingLanguage);

        canApply = true;
        await coordinator.FlushPendingAsync();

        Assert.Null(coordinator.PendingLanguage);
        Assert.Equal("system", l10n.RequestedLanguage);
    }

    [Fact]
    public async Task LanguageChangeCoordinator_NewRequestClearsStalePendingLanguage()
    {
        var canApply = false;
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var coordinator = new LanguageChangeCoordinator(l10n, canApplyNow: () => canApply);

        // Defer "en" while interacting
        await coordinator.RequestLanguageAsync("en");
        Assert.Equal("en", coordinator.PendingLanguage);

        // Interaction ends, user requests "ja" (back to current)
        canApply = true;
        await coordinator.RequestLanguageAsync("ja");

        Assert.Null(coordinator.PendingLanguage);
        Assert.Equal("ja", l10n.RequestedLanguage);

        // Defer "en" again, then after interaction ends, user requests "system"
        canApply = false;
        await coordinator.RequestLanguageAsync("en");
        canApply = true;
        await coordinator.RequestLanguageAsync("system");

        Assert.Null(coordinator.PendingLanguage);
        Assert.Equal("system", l10n.RequestedLanguage);
    }

    [Fact]
    public async Task LanguageChangeCoordinator_AppliesSecondChangeImmediatelyWhileFirstSaveIsInFlight()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var savedLanguages = new List<string>();
        var saveCount = 0;

        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var coordinator = new LanguageChangeCoordinator(
            l10n,
            saveSettingsAsync: async lang =>
            {
                var count = Interlocked.Increment(ref saveCount);
                if (count == 1)
                {
                    await firstSaveGate.Task;
                }
                savedLanguages.Add(lang);
            });

        await coordinator.RequestLanguageAsync("en");
        Assert.Equal("en", l10n.RequestedLanguage);

        // While first save is blocked, second request applies immediately
        await coordinator.RequestLanguageAsync("system");
        Assert.Equal("system", l10n.RequestedLanguage);
        Assert.Equal("ja", l10n.UiCulture.Name);

        firstSaveGate.SetResult();
        await coordinator.WaitForPendingSavesAsync();

        Assert.Equal("system", savedLanguages.Last());
    }

    [Fact]
    public void LanguageChange_RetainsActiveToolInstance_AndRetranslatesStatusAndHints()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var vm = new MainWindowViewModel(l10n);
        var originalTool = vm.ActiveToolItem!;

        Assert.Equal("選択", originalTool.Title);
        Assert.Equal("準備完了 — Vectorer (.NET 10 + Avalonia + SkiaSharp)", vm.StatusMessage);
        Assert.Equal("選択: 0 個", vm.SelectionSummary);

        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();

        Assert.Same(originalTool, vm.ActiveToolItem);
        Assert.Equal("Select", originalTool.Title);
        Assert.Equal("Ready — Vectorer (.NET 10 + Avalonia + SkiaSharp)", vm.StatusMessage);
        Assert.Equal("Selected: 0", vm.SelectionSummary);
        Assert.Contains("Multi-select", vm.ToolHint);
    }

    [Fact]
    public void LanguageChange_NotifiesSamplePluginContributions()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var vm = new MainWindowViewModel(l10n);

        var menu = Assert.Single(vm.PluginMenus, m => m.Id == "sample.menu.apply_sunset_shader");
        var tool = Assert.Single(vm.PluginToolbarItems, t => t.Id == "sample.toolbar.add_starburst");

        Assert.Equal("選択にサンセットウェーブ シェーダーを適用", menu.Title);
        Assert.Equal("スターバースト (プラグイン)", tool.Title);

        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Apply Sunset Wave Shader to Selection", menu.Title);
        Assert.Equal("Starburst (Plugin)", tool.Title);
    }

    [Fact]
    public void ExportPresentation_RetranslatesWithoutRegeneratingPreviewOrChangingDimensions()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var vm = new MainWindowViewModel(l10n);
        using var export = vm.CreatePngExportViewModel();
        export.Mode = PngExportMode.Selection;
        var preview = export.PreviewPngBytes;
        var width = export.TargetWidth;
        var height = export.TargetHeight;
        var notifications = new List<string?>();
        export.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);

        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("No selected objects to export", export.BaseDimensionsLabel);
        Assert.Equal("No selected objects to export", export.OutputSizeLabel);
        Assert.Contains(nameof(PngExportViewModel.OutputSizeLabel), notifications);
        Assert.Same(preview, export.PreviewPngBytes);
        Assert.Equal(width, export.TargetWidth);
        Assert.Equal(height, export.TargetHeight);

        export.Dispose();
        notifications.Clear();
        vm.SetLanguageCommand.Execute("ja");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(notifications);
    }

    [Fact]
    public void SatelliteResourceAssembly_LoadsJapaneseStringsWithoutFallback()
    {
        var jaSatellite = Path.Combine(AppContext.BaseDirectory, "ja", "Vectorer.App.resources.dll");
        Assert.True(File.Exists(jaSatellite), $"Missing Japanese satellite assembly: {jaSatellite}");
        Assert.True(new FileInfo(jaSatellite).Length > 0);

        var enCulture = CultureInfo.GetCultureInfo("en");
        var jaCulture = CultureInfo.GetCultureInfo("ja");
        var jaEntries = LoadResxEntries(Path.Combine(FindLocalizationDirectory(), "Strings.ja.resx"));

        foreach (var (key, expectedJa) in jaEntries)
        {
            var actualEn = Strings.ResourceManager.GetString(key, enCulture);
            var actualJa = Strings.ResourceManager.GetString(key, jaCulture);
            Assert.False(string.IsNullOrWhiteSpace(actualEn), $"English resource '{key}' returned empty.");
            Assert.Equal(expectedJa, actualJa);
        }

        // Verify a representative key is actually translated (not English fallback)
        Assert.NotEqual(
            Strings.ResourceManager.GetString(nameof(Strings.Menu_File), enCulture),
            Strings.ResourceManager.GetString(nameof(Strings.Menu_File), jaCulture));
    }

    private static string FindLocalizationDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Vectorer.App", "Localization");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate src/Vectorer.App/Localization");
    }

    private static Dictionary<string, string> LoadResxEntries(string path)
    {
        var doc = XDocument.Load(path);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root!.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            Assert.False(string.IsNullOrWhiteSpace(name), $"Empty key name in {path}");
            Assert.True(entries.TryAdd(name!, data.Element("value")?.Value ?? string.Empty), $"Duplicate key '{name}' in {path}");
        }
        return entries;
    }

    private static int[] ExtractFormatIndices(string value) =>
        FormatPlaceholderRegex().Matches(value)
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .OrderBy(i => i)
            .ToArray();
}
