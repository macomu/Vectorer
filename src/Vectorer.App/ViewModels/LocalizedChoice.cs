using CommunityToolkit.Mvvm.ComponentModel;
using Vectorer.App.Localization;

namespace Vectorer.App.ViewModels;

/// <summary>A stable model value with a separately translated presentation.</summary>
public sealed class LocalizedChoice(object value, string? key, AppLocalization localization) : ObservableObject
{
    public object Value { get; } = value;
    public string Label => key is null ? Value.ToString() ?? "" : localization.Get(key);
    public void RefreshTranslation() => OnPropertyChanged(nameof(Label));
}
