using System.Globalization;
using Avalonia.Threading;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
using Vectorer.App.Tools;
using Vectorer.App.ViewModels;
using Vectorer.Core.Geometry;
using Xunit;

namespace Vectorer.App.Tests;

[Collection("Localization")]
public sealed class LanguageChangeSafetyTests
{
    [Fact]
    public async Task Safety_TracksIndependentReasons_AndNativeDialogDoubleDisposeIsIdempotent()
    {
        var safety = new LanguageChangeSafety();
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var coordinator = new LanguageChangeCoordinator(l10n, safety: safety);

        var tokenA = new object();
        var tokenB = new object();
        safety.SetGestureActive(true);
        safety.SetInputGuard(tokenA, true);
        safety.SetInputGuard(tokenB, true);
        var outerDialog = safety.EnterNativeDialog();
        var innerDialog = safety.EnterNativeDialog();

        Assert.False(safety.CanApply);
        Assert.Equal(2, safety.NativeDialogDepth);

        await coordinator.RequestLanguageAsync("en");
        Assert.Equal("ja", l10n.RequestedLanguage);
        Assert.Equal("en", coordinator.PendingLanguage);

        // Double dispose of innerDialog only decrements depth once
        innerDialog.Dispose();
        innerDialog.Dispose();
        Assert.Equal(1, safety.NativeDialogDepth);
        Assert.False(safety.CanApply);
        Assert.Equal("ja", l10n.RequestedLanguage);

        // Releasing tokenA leaves tokenB, gesture, and outerDialog active
        safety.SetInputGuard(tokenA, false);
        Assert.True(safety.HasInputCompositionOrConservativeFocusGuard);
        Assert.False(safety.CanApply);

        outerDialog.Dispose();
        Assert.Equal(0, safety.NativeDialogDepth);
        Assert.False(safety.CanApply);

        safety.SetGestureActive(false);
        Assert.False(safety.HasActiveGesture);
        Assert.False(safety.CanApply);

        // Releasing the last reason automatically applies the pending language
        safety.SetInputGuard(tokenB, false);
        Assert.True(safety.CanApply);
        Assert.Null(coordinator.PendingLanguage);
        Assert.Equal("en", l10n.RequestedLanguage);
    }

    [Fact]
    public async Task Coordinator_HandlesPendingCancellation_Reentrancy_AndReblockBeforeFlush()
    {
        var safety = new LanguageChangeSafety();
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var scheduled = new Queue<Action>();
        var appliedHistory = new List<string>();

        LanguageChangeCoordinator? coordinator = null;
        coordinator = new LanguageChangeCoordinator(
            l10n,
            safety: safety,
            scheduleFlush: action => scheduled.Enqueue(action),
            onCultureApplied: (applied, culture) =>
            {
                appliedHistory.Add(culture.Name);
                if (culture.Name == "en")
                {
                    // Re-entrant request during notification must not recurse synchronously
                    _ = coordinator!.RequestLanguageAsync("ja");
                    Assert.Equal("en", l10n.RequestedLanguage);
                }
            });

        // Request "en" in safe state -> scheduled, then re-blocked before flush runs
        await coordinator.RequestLanguageAsync("en");
        Assert.Equal("en", coordinator.PendingLanguage);
        safety.SetGestureActive(true);
        Assert.Single(scheduled);
        scheduled.Dequeue().Invoke();

        // Must NOT have applied while blocked
        Assert.Equal("ja", l10n.RequestedLanguage);
        Assert.Equal("en", coordinator.PendingLanguage);

        // Requesting current language ("ja") while blocked cancels pending
        await coordinator.RequestLanguageAsync("ja");
        Assert.Null(coordinator.PendingLanguage);

        // Request "en" again, unblock, and run scheduled flush
        await coordinator.RequestLanguageAsync("en");
        safety.SetGestureActive(false);
        Assert.NotEmpty(scheduled);
        while (scheduled.Count > 0)
        {
            scheduled.Dequeue().Invoke();
        }

        // Re-entrant request to "ja" was coalesced and applied after "en"
        Assert.Equal(["en", "ja"], appliedHistory);
        Assert.Equal("ja", l10n.RequestedLanguage);
        Assert.Null(coordinator.PendingLanguage);
    }

    [Fact]
    public void ViewModel_GestureEndAndEscape_AutomaticallyResumesPendingLanguage()
    {
        var l10n = new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture, "ja");
        var vm = new MainWindowViewModel(l10n);

        // Start a rectangle drawing gesture
        vm.ChooseTool(DrawingTool.Rectangle);
        vm.BeginInteraction(new SKPoint(10, 10), shift: false);
        Assert.True(vm.IsInteracting);
        Assert.True(vm.LanguageSafety.HasActiveGesture);

        vm.SetLanguageCommand.Execute("en");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("ja", l10n.RequestedLanguage);
        Assert.True(vm.HasPendingLanguage);
        Assert.Equal("操作終了後に言語を変更します", vm.PendingLanguageNotice);

        // End gesture -> automatically resumes and applies "en"
        vm.UpdateInteraction(new SKPoint(80, 60), shift: false, alt: false);
        vm.EndInteraction();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.IsInteracting);
        Assert.False(vm.HasPendingLanguage);
        Assert.Equal(string.Empty, vm.PendingLanguageNotice);
        Assert.Equal("en", l10n.RequestedLanguage);

        // Now start a gesture and cancel via Escape (CancelInteraction)
        vm.BeginInteraction(new SKPoint(200, 200), shift: false);
        vm.SetLanguageCommand.Execute("ja");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("en", l10n.RequestedLanguage);
        Assert.True(vm.HasPendingLanguage);

        vm.CancelInteraction();
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.HasPendingLanguage);
        Assert.Equal("ja", l10n.RequestedLanguage);
    }
}
