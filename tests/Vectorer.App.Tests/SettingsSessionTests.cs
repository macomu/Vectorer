using System.Globalization;
using Avalonia.Threading;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

[Collection("Localization")]
public sealed class SettingsSessionTests
{
    private sealed class ControlledSettingsStore : IEditorUserSettingsStore
    {
        private readonly Queue<Func<EditorUserSettings, Task>> _handlers = new();
        private int _inFlight;
        public int MaxConcurrency { get; private set; }
        public List<EditorUserSettings> SavedSnapshots { get; } = [];
        public int SaveCallCount { get; private set; }

        public void EnqueueSaveHandler(Func<EditorUserSettings, Task> handler) => _handlers.Enqueue(handler);

        public Task<EditorUserSettings> LoadAsync() => Task.FromResult(new EditorUserSettings());

        public async Task SaveAsync(EditorUserSettings settings)
        {
            SaveCallCount++;
            var concurrent = Interlocked.Increment(ref _inFlight);
            MaxConcurrency = Math.Max(MaxConcurrency, concurrent);
            try
            {
                if (_handlers.Count > 0)
                {
                    await _handlers.Dequeue()(settings);
                }

                SavedSnapshots.Add(settings with { RecentFiles = (settings.RecentFiles ?? []).ToArray() });
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }
    }

    [Fact]
    public async Task Session_SerializesWrites_CoalescesInFlightUpdates_AndDefensivelyCopiesRecentFiles()
    {
        var store = new ControlledSettingsStore();
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.EnqueueSaveHandler(_ => firstGate.Task);

        var session = new EditorUserSettingsSession(store, new EditorUserSettings(300, null, "ja"));
        var mutableRecent = new List<RecentEditorFile> { new("a.svg", "file:///C:/a.svg") };

        // Start first write
        session.Update(s => s with { UiLanguage = "en" });
        Assert.Equal(1, session.Generation);
        Assert.Equal(0, session.PersistedGeneration);

        // While first write is blocked, update width, recent files, and language
        session.Update(s => s with { InspectorWidth = 420, RecentFiles = mutableRecent });
        mutableRecent.Add(new("mutated-after.svg", "file:///C:/mutated.svg"));
        session.Update(s => s with { UiLanguage = "system", InspectorWidth = 460 });

        Assert.Equal(3, session.Generation);
        Assert.Equal(0, session.PersistedGeneration);

        // Complete first write: generation 1 persisted, but generation 3 is still pending
        firstGate.SetResult();
        var result = await session.FlushAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, store.MaxConcurrency);
        Assert.Equal(3, session.PersistedGeneration);
        Assert.False(session.HasPendingChanges);

        var last = store.SavedSnapshots.Last();
        Assert.Equal("system", last.UiLanguage);
        Assert.Equal(460, last.InspectorWidth);
        Assert.Single(last.RecentFiles!);
        Assert.Equal("a.svg", last.RecentFiles![0].Name);
    }

    [Fact]
    public async Task Session_StopsOnFailure_RetriesOnSameLanguageUpdateOrFlush_AndKeepsUiLanguage()
    {
        var store = new ControlledSettingsStore();
        store.EnqueueSaveHandler(_ => throw new IOException("disk full"));
        store.EnqueueSaveHandler(_ => throw new UnauthorizedAccessException("still locked"));

        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var session = new EditorUserSettingsSession(store, new EditorUserSettings(300, null, "ja"));
        var vm = new MainWindowViewModel(l10n, session);
        vm.ApplyLoadedSettings(new EditorUserSettings(300, null, "ja"), store);

        // Switch to English; save fails once and stops (no infinite retry loop)
        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();
        await vm.LanguageCoordinator.WaitForPendingSavesAsync();

        Assert.Equal("en", l10n.RequestedLanguage);
        Assert.True(session.HasPendingChanges);
        Assert.True(vm.HasSettingsSaveError);
        Assert.False(vm.HasUnsavedChanges);
        Assert.Contains("Could not save history or display settings", vm.StatusMessage);
        Assert.Equal(1, store.SaveCallCount);

        // Selecting the SAME language again while dirty retries saving (fails a second time, still stops)
        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();
        await vm.LanguageCoordinator.WaitForPendingSavesAsync();
        Assert.Equal(2, store.SaveCallCount);
        Assert.True(vm.HasSettingsSaveError);

        // Third attempt (next handler succeeds) via FlushAsync / SaveUserSettingsAsync
        await vm.SaveUserSettingsAsync(390);
        Assert.Equal(3, store.SaveCallCount);
        Assert.False(session.HasPendingChanges);
        Assert.False(vm.HasSettingsSaveError);
        Assert.Equal("en", store.SavedSnapshots.Last().UiLanguage);
        Assert.Equal(390, store.SavedSnapshots.Last().InspectorWidth);
    }

    [Fact]
    public async Task Shutdown_DropsPendingUnappliedLanguage_AndDoesNotPersistIt()
    {
        var store = new ControlledSettingsStore();
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var session = new EditorUserSettingsSession(store, new EditorUserSettings(300, null, "ja"));
        var vm = new MainWindowViewModel(l10n, session);
        vm.ApplyLoadedSettings(new EditorUserSettings(300, null, "ja"), store);

        vm.LanguageSafety.SetGestureActive(true);
        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("en", vm.LanguageCoordinator.PendingLanguage);

        // Begin shutdown while "en" is still pending
        vm.LanguageCoordinator.BeginShutdown();
        Assert.Null(vm.LanguageCoordinator.PendingLanguage);

        await vm.SaveUserSettingsAsync(350);
        Assert.Single(store.SavedSnapshots);
        Assert.Equal("ja", store.SavedSnapshots[0].UiLanguage);
        Assert.Equal(350, store.SavedSnapshots[0].InspectorWidth);
    }
}
