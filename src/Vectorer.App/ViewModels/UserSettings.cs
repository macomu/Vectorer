using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Vectorer.App.Services;

namespace Vectorer.App.ViewModels;

public sealed record RecentFileMenuEntry(string Header, System.Windows.Input.ICommand? Command, object? Parameter, bool IsEnabled = true);

public sealed partial class MainWindowViewModel
{
    private IEditorUserSettingsStore? _userSettingsStore;
    public ObservableCollection<RecentEditorFile> RecentFiles { get; } = [];
    public bool HasRecentFiles => RecentFiles.Count > 0;
    public IEnumerable<RecentFileMenuEntry> RecentFileMenuItems => RecentFiles.Count == 0
        ? [new("履歴はありません", null, null, false)]
        : RecentFiles.Select(f => new RecentFileMenuEntry(f.DisplayName, OpenRecentFileCommand, f))
            .Append(new("履歴を消去", ClearRecentFilesCommand, null));
    public double InspectorWidth { get; private set; } = 300;

    public async Task LoadUserSettingsAsync(IEditorUserSettingsStore store)
    {
        _userSettingsStore = store;
        var settings = await store.LoadAsync();
        InspectorWidth = double.IsFinite(settings.InspectorWidth) ? Math.Clamp(settings.InspectorWidth, 240, 650) : 300;
        RecentFiles.Clear();
        foreach (var file in settings.RecentFiles ?? [])
            if (file is not null && !string.IsNullOrWhiteSpace(file.Name) && Uri.TryCreate(file.Location, UriKind.Absolute, out _)
                && !RecentFiles.Any(f => SameLocation(f.Location, file.Location)) && RecentFiles.Count < 10)
                RecentFiles.Add(file);
        OnPropertyChanged(nameof(HasRecentFiles)); OnPropertyChanged(nameof(RecentFileMenuItems));
    }

    private static bool SameLocation(string a, string b)
    {
        if (!Uri.TryCreate(a, UriKind.Absolute, out var first) || !Uri.TryCreate(b, UriKind.Absolute, out var second)) return a == b;
        return first.IsFile && second.IsFile
            ? string.Equals(first.LocalPath, second.LocalPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            : first.Equals(second);
    }

    public async Task SaveUserSettingsAsync(double? inspectorWidth = null)
    {
        if (inspectorWidth is { } width && double.IsFinite(width)) InspectorWidth = Math.Clamp(width, 240, 650);
        if (_userSettingsStore is null) return;
        try { await _userSettingsStore.SaveAsync(new(InspectorWidth, RecentFiles.ToArray())); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusMessage = $"履歴・表示設定を保存できませんでした: {ex.Message}"; }
    }

    private async Task RememberFileAsync(EditorFile file)
    {
        if (file.Location is not { } uri || !uri.IsAbsoluteUri) return;
        var location = uri.AbsoluteUri;
        for (var i = RecentFiles.Count - 1; i >= 0; i--)
            if (SameLocation(RecentFiles[i].Location, location)) RecentFiles.RemoveAt(i);
        RecentFiles.Insert(0, new(file.Name, location));
        while (RecentFiles.Count > 10) RecentFiles.RemoveAt(RecentFiles.Count - 1);
        OnPropertyChanged(nameof(HasRecentFiles)); OnPropertyChanged(nameof(RecentFileMenuItems));
        await SaveUserSettingsAsync();
    }

    [RelayCommand] private async Task ClearRecentFilesAsync()
    {
        RecentFiles.Clear(); OnPropertyChanged(nameof(HasRecentFiles)); OnPropertyChanged(nameof(RecentFileMenuItems)); await SaveUserSettingsAsync();
    }

    [RelayCommand] private async Task OpenRecentFileAsync(RecentEditorFile? recent)
    {
        if (recent is null || IsBusy || IsInteracting || FileDialogs is null) return;
        FinishTextEditing();
        if (!await ProtectUnsavedAsync()) return;
        IsBusy = true;
        try
        {
            var file = await FileDialogs.ResolveSvgAsync(new Uri(recent.Location, UriKind.Absolute));
            if (file is null) throw new FileNotFoundException($"ファイルが見つかりません: {recent.Name}");
            await OpenFileCoreAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or ArgumentException)
        { StatusMessage = $"開けませんでした: {ex.Message}"; await FileDialogs.ShowErrorAsync(StatusMessage); }
        finally { IsBusy = false; }
    }
}
