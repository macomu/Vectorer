using System.Globalization;

namespace Vectorer.App.Localization;

public sealed class LanguageChangeCoordinator
{
    private readonly AppLocalization _localization;
    private readonly Func<bool> _canApplyNow;
    private readonly Action<bool, CultureInfo>? _onCultureApplied;
    private readonly Func<string, Task>? _saveSettingsAsync;
    private readonly Action<Exception>? _onSaveFailed;

    private bool _isApplying;
    private long _requestedSaveGeneration;
    private string? _pendingSaveLanguage;
    private Task _saveLoopTask = Task.CompletedTask;
    private readonly object _saveLock = new();

    public string? PendingLanguage { get; private set; }

    public LanguageChangeCoordinator(
        AppLocalization localization,
        Func<bool>? canApplyNow = null,
        Action<bool, CultureInfo>? onCultureApplied = null,
        Func<string, Task>? saveSettingsAsync = null,
        Action<Exception>? onSaveFailed = null)
    {
        _localization = localization;
        _canApplyNow = canApplyNow ?? (() => true);
        _onCultureApplied = onCultureApplied;
        _saveSettingsAsync = saveSettingsAsync;
        _onSaveFailed = onSaveFailed;
    }

    public Task RequestLanguageAsync(string? requestedLanguage)
    {
        var normalized = AppLocalization.NormalizeLanguage(requestedLanguage);

        if (_isApplying || !_canApplyNow())
        {
            PendingLanguage = normalized;
            return Task.CompletedTask;
        }

        PendingLanguage = null;
        if (string.Equals(_localization.RequestedLanguage, normalized, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        ApplyCore(normalized);
        return Task.CompletedTask;
    }

    public Task FlushPendingAsync()
    {
        if (_isApplying || PendingLanguage is null || !_canApplyNow())
        {
            return Task.CompletedTask;
        }

        var next = PendingLanguage;
        PendingLanguage = null;
        if (!string.Equals(_localization.RequestedLanguage, next, StringComparison.Ordinal))
        {
            ApplyCore(next);
        }

        return Task.CompletedTask;
    }

    public Task WaitForPendingSavesAsync()
    {
        lock (_saveLock)
        {
            return _saveLoopTask;
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

            EnqueueSave(normalizedLanguage);
        }
        finally
        {
            _isApplying = false;
        }

        if (PendingLanguage is not null && _canApplyNow())
        {
            _ = FlushPendingAsync();
        }
    }

    private void EnqueueSave(string normalizedLanguage)
    {
        if (_saveSettingsAsync is null)
        {
            return;
        }

        lock (_saveLock)
        {
            _requestedSaveGeneration++;
            _pendingSaveLanguage = normalizedLanguage;
            if (_saveLoopTask.IsCompleted)
            {
                _saveLoopTask = RunSaveLoopAsync();
            }
        }
    }

    private async Task RunSaveLoopAsync()
    {
        while (true)
        {
            string languageToSave;
            long generation;
            lock (_saveLock)
            {
                if (_pendingSaveLanguage is null)
                {
                    return;
                }

                languageToSave = _pendingSaveLanguage;
                generation = _requestedSaveGeneration;
                _pendingSaveLanguage = null;
            }

            try
            {
                await _saveSettingsAsync!(languageToSave);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _onSaveFailed?.Invoke(ex);
            }

            lock (_saveLock)
            {
                if (_pendingSaveLanguage is null && _requestedSaveGeneration == generation)
                {
                    return;
                }
            }
        }
    }
}
