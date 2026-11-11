using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (DataContext is MainWindowViewModel vm) { vm.FileDialogs = new EditorFileDialogs(this); vm.Clipboard = new EditorClipboard(this); vm.CloseRequested += Close; } };
        KeyDown += (_, e) =>
        {
            if (e.Handled || DataContext is not MainWindowViewModel vm) return;
            var focused = FocusManager?.GetFocusedElement() as Avalonia.Visual;
            if (focused is not null && focused.GetVisualAncestors().Prepend(focused).Any(v => v is TextBox or NumericUpDown or ComboBox)) return;
            e.Handled = vm.TryHandleEditorShortcut(e.Key, e.KeyModifiers);
        };
        Closing += async (_, e) =>
        {
            if (_allowClose || DataContext is not MainWindowViewModel vm || !vm.HasUnsavedChanges && !vm.IsBusy && !vm.IsTextEditing) return;
            e.Cancel = true;
            if (await vm.CanCloseAsync()) { _allowClose = true; Close(); }
        };
    }
}
