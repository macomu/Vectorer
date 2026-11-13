using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Vectorer.App.Services;

namespace Vectorer.App.Localization;

public sealed class LanguageChangeCoordinator : ObservableObject
{
    private readonly AppLocalization _localization;
    private readonly Func<bool> _canApplyNow;
    private readonly Action<bool, CultureInfo>? _onCultureApplied;
    private readonly EditorUserSettingsSession? _settingsSession;
    private readonly Func<string, Task>? _saveSettingsAsync;
    private readonly Action<Exception>? _onSaveFailed;
    private readonly LanguageChangeSafety? _safety;
    private readonly Action<Action> _scheduleFlush;

    private bool _isApplying;
    private bool _flushScheduled;
    private bool _isShuttingDown;
    private string? _pendingLanguage;
    private Task _legacySaveTask = Task.CompletedTask;

    public string? PendingLanguage
    {
        get => _pendingLanguage;
        private set
        {
            if (string.Equals(_pendingLanguage, value, StringComparison.Ordinal))
            {
                return;
            }

            _pendingLanguage = value;
            OnPropertyChanged();
        }
    }

    public LanguageChangeCoordinator(
        AppLocalization localization,
        Func<bool>? canApplyNow = null,
        Action<bool, CultureInfo>? onCultureApplied = null,
        Func<string, Task>? saveSettingsAsync = null,
        Action<Exception>? onSaveFailed = null,
        LanguageChangeSafety? safety = null,
        Action<Action>? scheduleFlush = null,
        EditorUserSettingsSession? settingsSession = null)
    {
        _localization = localization;
        _canApplyNow = canApplyNow ?? (() => true);
        _onCultureApplied = onCultureApplied;
        _saveSettingsAsync = saveSettingsAsync;
        _onSaveFailed = onSaveFailed;
        _safety = safety;
        _scheduleFlush = scheduleFlush ?? (action => action());
        _settingsSession = settingsSession;

        if (_safety is not null)
        {
            _safety.StateChanged += OnSafetyStateChanged;
        }
    }

    private bool CanApplyNow() => !_isShuttingDown && (_safety?.CanApply ?? true) && _canApplyNow();

    public void BeginShutdown()
    {
        _isShuttingDown = true;
        PendingLanguage = null;
    }

    public Task RequestLanguageAsync(string? requestedLanguage)
    {
        if (_isShuttingDown)
        {
            return Task.CompletedTask;
        }

        var normalized = AppLocalization.NormalizeLanguage(requestedLanguage);

        if (string.Equals(_localization.RequestedLanguage, normalized, StringComparison.Ordinal))
        {
            PendingLanguage = null;
            if (_settingsSession is { HasPendingChanges: true })
            {
                _settingsSession.RetrySave();
            }
            return Task.CompletedTask;
        }

        PendingLanguage = normalized;

        if (!_isApplying && CanApplyNow())
        {
            ScheduleFlush();
        }

        return Task.CompletedTask;
    }

    public Task FlushPendingAsync()
    {
        FlushScheduledCore();
        return Task.CompletedTask;
    }

    public Task WaitForPendingSavesAsync()
    {
        if (_settingsSession is not null)
        {
            return _settingsSession.WaitForIdleAsync();
        }

        return _legacySaveTask;
    }

    private void OnSafetyStateChanged(object? sender, EventArgs e)
    {
        if (!_isApplying && PendingLanguage is not null && CanApplyNow())
        {
            ScheduleFlush();
        }
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled || _isShuttingDown)
        {
            return;
        }

        _flushScheduled = true;
        _scheduleFlush(FlushScheduledCore);
    }

    private void FlushScheduledCore()
    {
        _flushScheduled = false;

        if (_isShuttingDown || _isApplying || PendingLanguage is null || !CanApplyNow())
        {
            return;
        }

        var next = PendingLanguage;
        PendingLanguage = null;
        if (!string.Equals(_localization.RequestedLanguage, next, StringComparison.Ordinal))
        {
            ApplyCore(next);
        }
    }

    private void ApplyCore(string normalizedLanguage)
    {
        _isApplying = true;
        try
        {
            var targetCulture = AppLocalization.ResolveCulture(normalizedLanguage, _localization.OsUiCulture);
            var cultureChanged = _localization.ApplyCulture(normalizedLanguage, targetCulture);

            if (cultureChanged)
            {
                _onCultureApplied?.Invoke(cultureChanged, targetCulture);
            }

            if (_settingsSession is not null)
            {
                _settingsSession.Update(s => s with { UiLanguage = normalizedLanguage });
            }
            else if (_saveSettingsAsync is not null)
            {
                _legacySaveTask = SaveLegacyAsync(normalizedLanguage);
            }
        }
        finally
        {
            _isApplying = false;
        }

        if (PendingLanguage is not null && CanApplyNow())
        {
            ScheduleFlush();
        }
    }

    private async Task SaveLegacyAsync(string normalizedLanguage)
    {
        try
        {
            await _saveSettingsAsync!(normalizedLanguage).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _onSaveFailed?.Invoke(ex);
        }
    }
}
