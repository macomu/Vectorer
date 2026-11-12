using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Localization;

/// <summary>Owns the bindings for a programmatically created dialog's presentation.</summary>
public sealed class LocalizedControlBindings(AppLocalization localization) : IDisposable
{
    private readonly List<IDisposable> _bindings = [];
    private readonly List<Action> _cleanup = [];

    public T Bind<T>(T control, AvaloniaProperty property, string resourceProperty) where T : AvaloniaObject
    {
        _bindings.Add(control.Bind(property, new Binding(resourceProperty)
        {
            Source = localization,
            Mode = BindingMode.OneWay
        }));
        return control;
    }

    public void Dispose()
    {
        foreach (var binding in _bindings) binding.Dispose();
        _bindings.Clear();
        foreach (var cleanup in _cleanup) cleanup();
        _cleanup.Clear();
    }

    public void BindChoices(ComboBox control, IEnumerable<LocalizedChoice> items)
    {
        var choices = items.ToArray();
        control.ItemsSource = choices;
        control.SelectedValueBinding = new Binding(nameof(LocalizedChoice.Value));
        control.ItemTemplate = new FuncDataTemplate<LocalizedChoice>((item, _) =>
        {
            var label = new TextBlock();
            _bindings.Add(label.Bind(TextBlock.TextProperty, new Binding(nameof(LocalizedChoice.Label)) { Source = item }));
            return label;
        });
        void Refresh(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            foreach (var choice in choices) choice.RefreshTranslation();
        }
        localization.PropertyChanged += Refresh;
        _cleanup.Add(() => localization.PropertyChanged -= Refresh);
    }

    public T BindMessage<T>(T control, AvaloniaProperty property, LocalizedMessage message) where T : AvaloniaObject
    {
        _bindings.Add(control.Bind(property, new Binding(nameof(AppLocalization.Revision))
        {
            Source = localization,
            Mode = BindingMode.OneWay,
            Converter = new FuncValueConverter<long, string>(_ => localization.Format(message))
        }));
        return control;
    }
}
