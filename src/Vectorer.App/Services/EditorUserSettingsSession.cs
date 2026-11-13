namespace Vectorer.App.Services;

public enum SettingsFlushOutcome
{
    NothingToSave,
    Saved,
    Failed
}

public sealed record SettingsFlushResult(
    SettingsFlushOutcome Outcome,
    long PersistedGeneration,
    long TargetGeneration,
    Exception? Error = null)
{
    public bool Succeeded => Outcome != SettingsFlushOutcome.Failed;
}

/// <summary>
/// Owns the authoritative in-memory <see cref="EditorUserSettings"/> snapshot, tracks dirty
/// generations, and serializes background writes through a single non-overlapping writer.
/// </summary>
public sealed class EditorUserSettingsSession
{
    private readonly object _sync = new();
    private readonly Action<Action> _postNotification;
    private readonly Action<Exception>? _onSaveFailed;
    private readonly Action? _onSaveSucceeded;

    private IEditorUserSettingsStore? _store;
    private EditorUserSettings _current;
    private long _generation;
    private long _persistedGeneration;
    private Exception? _lastSaveError;
    private bool _writerRunning;
    private Task _writerTask = Task.CompletedTask;

    public event EventHandler<Exception>? SaveFailed;
    public event EventHandler? SaveSucceeded;

    public EditorUserSettingsSession(
        IEditorUserSettingsStore? store = null,
        EditorUserSettings? initialSettings = null,
        Action<Action>? postNotification = null,
        Action<Exception>? onSaveFailed = null,
        Action? onSaveSucceeded = null)
    {
        _store = store;
        _current = DefensiveCopy(initialSettings ?? new EditorUserSettings());
        _postNotification = postNotification ?? (action => action());
        _onSaveFailed = onSaveFailed;
        _onSaveSucceeded = onSaveSucceeded;
    }

    public EditorUserSettings Current
    {
        get { lock (_sync) return _current; }
    }

    public long Generation
    {
        get { lock (_sync) return _generation; }
    }

    public long PersistedGeneration
    {
        get { lock (_sync) return _persistedGeneration; }
    }

    public bool HasPendingChanges
    {
        get { lock (_sync) return _generation > _persistedGeneration; }
    }

    public Exception? LastSaveError
    {
        get { lock (_sync) return _lastSaveError; }
    }

    public void AttachLoaded(EditorUserSettings settings, IEditorUserSettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        lock (_sync)
        {
            _store = store;
            _current = DefensiveCopy(settings);
            _persistedGeneration = _generation;
            _lastSaveError = null;
        }
    }

    public void Update(Func<EditorUserSettings, EditorUserSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_sync)
        {
            var next = DefensiveCopy(update(_current));
            if (!AreEqual(_current, next))
            {
                _current = next;
                _generation++;
            }

            if (_store is not null && _generation > _persistedGeneration && !_writerRunning)
            {
                StartWriterLocked();
            }
        }
    }

    public void RetrySave()
    {
        lock (_sync)
        {
            if (_store is not null && _generation > _persistedGeneration && !_writerRunning)
            {
                StartWriterLocked();
            }
        }
    }

    public async Task<SettingsFlushResult> FlushAsync()
    {
        Task writerTask;
        long targetGeneration;

        lock (_sync)
        {
            targetGeneration = _generation;
            if (_store is null || _persistedGeneration >= _generation)
            {
                return new SettingsFlushResult(SettingsFlushOutcome.NothingToSave, _persistedGeneration, targetGeneration);
            }

            if (!_writerRunning)
            {
                StartWriterLocked();
            }

            writerTask = _writerTask;
        }

        await writerTask.ConfigureAwait(false);

        lock (_sync)
        {
            if (_persistedGeneration >= targetGeneration && _lastSaveError is null)
            {
                return new SettingsFlushResult(SettingsFlushOutcome.Saved, _persistedGeneration, targetGeneration);
            }

            return new SettingsFlushResult(
                _lastSaveError is not null ? SettingsFlushOutcome.Failed : SettingsFlushOutcome.Saved,
                _persistedGeneration,
                targetGeneration,
                _lastSaveError);
        }
    }

    public Task WaitForIdleAsync()
    {
        lock (_sync)
        {
            return _writerTask;
        }
    }

    private void StartWriterLocked()
    {
        var previous = _writerTask;
        _writerRunning = true;
        _writerTask = RunWriterAsync(previous);
    }

    private async Task RunWriterAsync(Task previous)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch
        {
            // Previous writer exceptions are already recorded on _lastSaveError.
        }

        while (true)
        {
            EditorUserSettings snapshot;
            long targetGeneration;
            IEditorUserSettingsStore? store;

            lock (_sync)
            {
                if (_store is null || _persistedGeneration >= _generation)
                {
                    _writerRunning = false;
                    return;
                }

                snapshot = DefensiveCopy(_current);
                targetGeneration = _generation;
                store = _store;
            }

            try
            {
                await store.SaveAsync(snapshot).ConfigureAwait(false);

                var clearedError = false;
                lock (_sync)
                {
                    if (targetGeneration > _persistedGeneration)
                    {
                        _persistedGeneration = targetGeneration;
                    }

                    if (_lastSaveError is not null)
                    {
                        _lastSaveError = null;
                        clearedError = true;
                    }

                    if (_persistedGeneration >= _generation)
                    {
                        _writerRunning = false;
                        if (clearedError)
                        {
                            _postNotification(() =>
                            {
                                _onSaveSucceeded?.Invoke();
                                SaveSucceeded?.Invoke(this, EventArgs.Empty);
                            });
                        }
                        return;
                    }
                }

                if (clearedError)
                {
                    _postNotification(() =>
                    {
                        _onSaveSucceeded?.Invoke();
                        SaveSucceeded?.Invoke(this, EventArgs.Empty);
                    });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lock (_sync)
                {
                    _lastSaveError = ex;
                    _writerRunning = false;
                }

                _postNotification(() =>
                {
                    _onSaveFailed?.Invoke(ex);
                    SaveFailed?.Invoke(this, ex);
                });
                return;
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _lastSaveError = ex;
                    _writerRunning = false;
                }

                _postNotification(() =>
                {
                    _onSaveFailed?.Invoke(ex);
                    SaveFailed?.Invoke(this, ex);
                });
                return;
            }
        }
    }

    private static EditorUserSettings DefensiveCopy(EditorUserSettings settings) =>
        settings with { RecentFiles = (settings.RecentFiles ?? []).ToArray() };

    private static bool AreEqual(EditorUserSettings left, EditorUserSettings right)
    {
        if (Math.Abs(left.InspectorWidth - right.InspectorWidth) > 0.0001
            || !string.Equals(left.UiLanguage, right.UiLanguage, StringComparison.Ordinal))
        {
            return false;
        }

        var a = left.RecentFiles ?? [];
        var b = right.RecentFiles ?? [];
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
