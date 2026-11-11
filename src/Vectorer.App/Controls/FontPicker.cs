using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace Vectorer.App.Controls;

/// <summary>Editable font name plus a searchable, virtualized dropdown.</summary>
public sealed class FontPicker : UserControl
{
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<FontPicker, string>(nameof(Value), "sans-serif", defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<IReadOnlyList<string>> FamiliesProperty =
        AvaloniaProperty.Register<FontPicker, IReadOnlyList<string>>(nameof(Families), []);
    public string Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IReadOnlyList<string> Families { get => GetValue(FamiliesProperty); set => SetValue(FamiliesProperty, value); }
    private readonly ListBox _list = new() { Height = 300, MinWidth = 300 };
    private readonly TextBox _search = new() { Watermark = "フォント名で検索" };

    public FontPicker()
    {
        var editor = new TextBox { MinWidth = 160 };
        ToolTip.SetTip(editor, "フォント名（直接入力も可能）");
        editor.Bind(TextBox.TextProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });
        var content = new StackPanel { Spacing = 6, Width = 340 };
        content.Children.Add(_search); content.Children.Add(_list);
        var flyout = new Flyout { Content = content };
        var button = new Button { Content = "▾", Flyout = flyout, Padding = new Thickness(7, 4) };
        ToolTip.SetTip(button, "インストール済みフォントを選択");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(button, 1); row.Children.Add(editor); row.Children.Add(button); Content = row;
        _search.TextChanged += (_, _) => Filter();
        flyout.Opened += (_, _) =>
        {
            _list.SelectedItem = null; _search.Text = ""; Filter(); _search.Focus();
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is not string family) return;
            SetCurrentValue(ValueProperty, family); flyout.Hide();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FamiliesProperty && _list is not null) Filter();
    }

    private void Filter() => _list.ItemsSource = Families.Where(f =>
        f.Contains(_search.Text ?? "", StringComparison.CurrentCultureIgnoreCase)).ToArray();
}
