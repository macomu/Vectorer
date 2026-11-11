using SkiaSharp;

namespace Vectorer.App.Services;

public interface IInstalledFontCatalog
{
    Task<IReadOnlyList<string>> GetFamiliesAsync();
}

/// <summary>Use the same font backend as glyph rendering; enumerate once away from the UI thread.</summary>
public sealed class InstalledFontCatalog : IInstalledFontCatalog
{
    private static readonly Lazy<Task<IReadOnlyList<string>>> Families = new(() => Task.Run<IReadOnlyList<string>>(() =>
        new[] { "sans-serif", "serif", "monospace" }.Concat(SKFontManager.Default.GetFontFamilies())
            .Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.CurrentCultureIgnoreCase).ToArray()));

    public Task<IReadOnlyList<string>> GetFamiliesAsync() => Families.Value;
}
