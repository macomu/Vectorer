using Avalonia.Controls;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (DataContext is MainWindowViewModel vm) vm.FileDialogs = new EditorFileDialogs(this); };
        Closing += async (_, e) =>
        {
            if (_allowClose || DataContext is not MainWindowViewModel vm || !vm.HasUnsavedChanges && !vm.IsBusy && !vm.IsTextEditing) return;
            e.Cancel = true;
            if (await vm.CanCloseAsync()) { _allowClose = true; Close(); }
        };
    }
}
