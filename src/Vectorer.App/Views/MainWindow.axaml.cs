using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Views;

public partial class MainWindow : Window
{
    private bool _allowClose;
    private bool _checkingClose;
    private SceneTreeItem? _dragTreeSource;
    private Point _dragStartPoint;
    private bool _isDraggingTreeItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.FileDialogs = new EditorFileDialogs(this);
                vm.Clipboard = new EditorClipboard(this);
                vm.CloseRequested += Close;
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Handled || DataContext is not MainWindowViewModel vm) return;
            var focused = FocusManager?.GetFocusedElement() as Visual;
            if (focused is TextBox renameBox && renameBox.DataContext is SceneTreeItem renameItem && renameItem.IsEditingName)
            {
                if (e.Key == Key.Enter)
                {
                    vm.CommitRenameNodeCommand.Execute(renameItem);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Escape)
                {
                    vm.CancelRenameNodeCommand.Execute(renameItem);
                    e.Handled = true;
                    return;
                }
            }

            if (focused is not null && focused.GetVisualAncestors().Prepend(focused).Any(v => v is TextBox or NumericUpDown or ComboBox)) return;
            e.Handled = vm.TryHandleEditorShortcut(e.Key, e.KeyModifiers);
        };

        Closing += async (_, e) =>
        {
            if (_allowClose || DataContext is not MainWindowViewModel vm) return;
            e.Cancel = true;
            if (_checkingClose) return;
            _checkingClose = true;
            try
            {
                if (await vm.CanCloseAsync())
                {
                    await vm.SaveUserSettingsAsync(EditorLayout.ColumnDefinitions[2].ActualWidth);
                    _allowClose = true; Close();
                }
            }
            finally { _checkingClose = false; }
        };

        SetupSceneTreeInteractions();
        Opened += async (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.IsBusy = true;
            try
            {
                await vm.LoadUserSettingsAsync(new JsonEditorUserSettingsStore());
                EditorLayout.ColumnDefinitions[2].Width = new GridLength(vm.InspectorWidth);
                await vm.LoadInstalledFontsAsync(new InstalledFontCatalog());
            }
            finally { vm.IsBusy = false; }
        };
    }

    private void SetupSceneTreeInteractions()
    {
        SceneHierarchyTree.DoubleTapped += (_, e) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if ((e.Source as Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
            var item = FindSceneTreeItem(e.Source as Visual);
            if (item is not null)
            {
                vm.BeginRenameNodeCommand.Execute(item);
                e.Handled = true;
            }
        };

        SceneHierarchyTree.AddHandler(LostFocusEvent, (_, e) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (e.Source is TextBox tb && tb.DataContext is SceneTreeItem item && item.IsEditingName)
            {
                vm.CommitRenameNodeCommand.Execute(item);
            }
        }, RoutingStrategies.Bubble);

        SceneHierarchyTree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (!e.GetCurrentPoint(SceneHierarchyTree).Properties.IsLeftButtonPressed) return;
            if ((e.Source as Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
            if ((e.Source as Visual)?.GetVisualAncestors().OfType<TextBox>().Any() == true) return;
            _dragTreeSource = FindSceneTreeItem(e.Source as Visual);
            _dragStartPoint = e.GetPosition(SceneHierarchyTree);
            _isDraggingTreeItem = false;
        }, RoutingStrategies.Tunnel);

        SceneHierarchyTree.AddHandler(PointerMovedEvent, (_, e) =>
        {
            if (_dragTreeSource is null || !e.GetCurrentPoint(SceneHierarchyTree).Properties.IsLeftButtonPressed) return;
            var pos = e.GetPosition(SceneHierarchyTree);
            if (Math.Abs(pos.X - _dragStartPoint.X) > 5 || Math.Abs(pos.Y - _dragStartPoint.Y) > 5)
            {
                _isDraggingTreeItem = true;
            }
        }, RoutingStrategies.Tunnel);

        SceneHierarchyTree.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (_dragTreeSource is not null && _isDraggingTreeItem && DataContext is MainWindowViewModel vm)
            {
                var pos = e.GetPosition(SceneHierarchyTree);
                var hitVisual = SceneHierarchyTree.InputHitTest(pos) as Visual;
                var targetContainer = hitVisual?.GetVisualAncestors().Prepend(hitVisual).OfType<TreeViewItem>().FirstOrDefault();
                if (targetContainer?.DataContext is SceneTreeItem targetItem && !ReferenceEquals(_dragTreeSource, targetItem))
                {
                    var localY = e.GetPosition(targetContainer).Y;
                    var height = Math.Max(1.0, targetContainer.Bounds.Height);
                    var ratio = localY / height;
                    var dropPos = ratio < 0.28
                        ? TreeDropPosition.Before
                        : (targetItem.IsContainer && ratio < 0.72 ? TreeDropPosition.Inside : TreeDropPosition.After);
                    vm.TryMoveTreeNode(_dragTreeSource.Node, targetItem.Node, dropPos);
                }
            }

            _dragTreeSource = null;
            _isDraggingTreeItem = false;
        }, RoutingStrategies.Tunnel);
    }

    private static SceneTreeItem? FindSceneTreeItem(Visual? visual) =>
        visual?.GetVisualAncestors().Prepend(visual).Select(v => v.DataContext).OfType<SceneTreeItem>().FirstOrDefault();
}
