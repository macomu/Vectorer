using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vectorer.App.Localization;

namespace Vectorer.App.Services;

/// <summary>
/// Conservatively defers UI language changes while an editable text input (TextBox, or the
/// internal TextBox of NumericUpDown / editable ComboBox) holds keyboard focus in a TopLevel.
/// Releases after natural focus departure once queued input commits complete on the UI dispatcher.
/// </summary>
public sealed class EditorInputLanguageGuard : IDisposable
{
    private readonly TopLevel _topLevel;
    private readonly LanguageChangeSafety _safety;
    private readonly object _ownerToken = new();
    private readonly EventHandler<RoutedEventArgs> _gotFocusHandler;
    private readonly EventHandler<RoutedEventArgs> _lostFocusHandler;
    private readonly EventHandler? _closedHandler;
    private long _releaseGeneration;
    private bool _disposed;

    private EditorInputLanguageGuard(TopLevel topLevel, LanguageChangeSafety safety)
    {
        _topLevel = topLevel;
        _safety = safety;

        _gotFocusHandler = OnGotFocus;
        _lostFocusHandler = OnLostFocus;

        _topLevel.AddHandler(InputElement.GotFocusEvent, _gotFocusHandler, RoutingStrategies.Bubble, handledEventsToo: true);
        _topLevel.AddHandler(InputElement.LostFocusEvent, _lostFocusHandler, RoutingStrategies.Bubble, handledEventsToo: true);

        if (_topLevel is Window window)
        {
            _closedHandler = (_, _) => Dispose();
            window.Closed += _closedHandler;
        }
        else
        {
            _topLevel.DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        if (IsEditableTextInput(_topLevel.FocusManager?.GetFocusedElement() as Visual))
        {
            _safety.SetInputGuard(_ownerToken, true);
        }
    }

    public static EditorInputLanguageGuard Attach(TopLevel topLevel, LanguageChangeSafety safety)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        ArgumentNullException.ThrowIfNull(safety);
        return new EditorInputLanguageGuard(topLevel, safety);
    }

    public static bool IsEditableTextInput(Visual? visual)
    {
        if (visual is null)
        {
            return false;
        }

        var textBox = visual as TextBox ?? visual.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        return textBox is { IsReadOnly: false, IsEnabled: true };
    }

    private void OnGotFocus(object? sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (IsEditableTextInput(e.Source as Visual))
        {
            _releaseGeneration++;
            _safety.SetInputGuard(_ownerToken, true);
        }
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (!IsEditableTextInput(e.Source as Visual))
        {
            return;
        }

        var generation = ++_releaseGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || generation != _releaseGeneration)
            {
                return;
            }

            if (IsEditableTextInput(_topLevel.FocusManager?.GetFocusedElement() as Visual))
            {
                _safety.SetInputGuard(_ownerToken, true);
                return;
            }

            _safety.SetInputGuard(_ownerToken, false);
        }, DispatcherPriority.Input);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _releaseGeneration++;
        _topLevel.RemoveHandler(InputElement.GotFocusEvent, _gotFocusHandler);
        _topLevel.RemoveHandler(InputElement.LostFocusEvent, _lostFocusHandler);

        if (_topLevel is Window window && _closedHandler is not null)
        {
            window.Closed -= _closedHandler;
        }
        else
        {
            _topLevel.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        }

        _safety.SetInputGuard(_ownerToken, false);
    }
}
