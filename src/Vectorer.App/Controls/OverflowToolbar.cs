using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Metadata;

namespace Vectorer.App.Controls;

/// <summary>Moves whole editor groups into a flyout only when they cannot fit.</summary>
public sealed class OverflowToolbar : UserControl
{
    private readonly StackPanel _primary = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _overflow = new() { Spacing = 10, Margin = new Thickness(4) };
    private readonly Button _more = new() { Content = "⋯", Width = 36, VerticalAlignment = VerticalAlignment.Center };

    [Content] public Avalonia.Controls.Controls Items { get; } = [];

    public OverflowToolbar()
    {
        var popup = new Flyout { Content = new ScrollViewer { Content = _overflow, MaxHeight = 500,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } };
        _more.Flyout = popup;
        ToolTip.SetTip(_more, "その他のツール設定");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(_more, 1);
        grid.Children.Add(_primary);
        grid.Children.Add(_more);
        Content = grid;
        Items.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
                foreach (Control item in e.OldItems) { _primary.Children.Remove(item); _overflow.Children.Remove(item); }
            if (e.NewItems is not null)
                foreach (Control item in e.NewItems)
                {
                    _primary.Children.Add(item);
                    item.PropertyChanged += (_, change) =>
                    {
                        if (change.Property == IsVisibleProperty) InvalidateMeasure();
                    };
                }
            InvalidateMeasure();
        };
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Items.Where(i => i.IsVisible).ToArray();
        foreach (var item in visible) item.Measure(Size.Infinity);
        var total = visible.Sum(i => i.DesiredSize.Width) + Math.Max(0, visible.Length - 1) * 8;
        var budget = Math.Max(0, availableSize.Width - (total > availableSize.Width ? 44 : 0));
        var used = 0d;
        foreach (var item in Items)
        {
            var width = item.IsVisible ? item.DesiredSize.Width + (used > 0 ? 8 : 0) : 0;
            var fits = !item.IsVisible || used + width <= budget;
            var target = fits ? _primary : _overflow;
            var other = fits ? _overflow : _primary;
            if (!target.Children.Contains(item))
            {
                other.Children.Remove(item);
                target.Children.Add(item);
            }
            if (fits) used += width;
        }
        // Keep the same ordering when width or active tool changes.
        Order(_primary); Order(_overflow);
        _more.IsVisible = _overflow.Children.Any(i => i.IsVisible);
        if (!_more.IsVisible) _more.Flyout?.Hide();
        return base.MeasureOverride(availableSize);
    }

    private void Order(StackPanel panel)
    {
        var index = 0;
        foreach (var item in Items.Where(panel.Children.Contains))
        {
            var current = panel.Children.IndexOf(item);
            if (current != index) panel.Children.Move(current, index);
            index++;
        }
    }
}

