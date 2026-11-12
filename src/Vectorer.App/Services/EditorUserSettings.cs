using System.Text.Json;

namespace Vectorer.App.Services;

public sealed record RecentEditorFile(string Name, string Location)
{
    public string DisplayName => Uri.TryCreate(Location, UriKind.Absolute, out var uri) && uri.IsFile
        ? $"{Name} — {Path.GetDirectoryName(uri.LocalPath)}" : $"{Name} — {Location}";
}

public sealed record EditorUserSettings(double InspectorWidth = 300, IReadOnlyList<RecentEditorFile>? RecentFiles = null, string UiLanguage = "system");

public interface IEditorUserSettingsStore
{
    Task<EditorUserSettings> LoadAsync();
    Task SaveAsync(EditorUserSettings settings);
}

/// <summary>Per-user preferences, separate from document data. Writes replace the complete file atomically.</summary>
public sealed class JsonEditorUserSettingsStore(string? filePath = null) : IEditorUserSettingsStore
{
    private readonly string _filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vectorer", "settings.json");
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<EditorUserSettings> LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!File.Exists(_filePath)) return new();
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<EditorUserSettings>(stream) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(EditorUserSettings settings)
    {
        await _gate.WaitAsync();
        var temporary = _filePath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
            {
                await JsonSerializer.SerializeAsync(stream, settings);
                await stream.FlushAsync();
            }
            File.Move(temporary, _filePath, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
