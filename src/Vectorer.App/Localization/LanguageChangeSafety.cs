namespace Vectorer.App.Localization;

/// <summary>
/// Tracks UI-thread safety conditions that defer immediate UI language switching
/// (active canvas gestures, conservative text-input focus/composition guards, and native OS dialogs).
/// </summary>
public sealed class LanguageChangeSafety
{
    private readonly HashSet<object> _inputGuards = new(ReferenceEqualityComparer.Instance);
    private bool _hasActiveGesture;
    private int _nativeDialogDepth;

    public bool HasActiveGesture => _hasActiveGesture;
    public bool HasInputCompositionOrConservativeFocusGuard => _inputGuards.Count > 0;
    public int NativeDialogDepth => _nativeDialogDepth;
    public bool CanApply => !HasActiveGesture && !HasInputCompositionOrConservativeFocusGuard && NativeDialogDepth == 0;

    public event EventHandler? StateChanged;

    public void SetGestureActive(bool active)
    {
        if (_hasActiveGesture == active)
        {
            return;
        }

        _hasActiveGesture = active;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetInputGuard(object ownerToken, bool active)
    {
        ArgumentNullException.ThrowIfNull(ownerToken);

        var changed = active
            ? _inputGuards.Add(ownerToken)
            : _inputGuards.Remove(ownerToken);

        if (changed)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IDisposable EnterNativeDialog()
    {
        _nativeDialogDepth++;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return new NativeDialogScope(this);
    }

    private void ExitNativeDialog()
    {
        if (_nativeDialogDepth <= 0)
        {
            return;
        }

        _nativeDialogDepth--;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class NativeDialogScope(LanguageChangeSafety owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.ExitNativeDialog();
            }
        }
    }
}
