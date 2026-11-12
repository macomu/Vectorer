using CommunityToolkit.Mvvm.ComponentModel;
using Vectorer.Abstractions.Plugins;

namespace Vectorer.App.ViewModels;

public sealed class PluginMenuItem(IMenuContribution contribution) : ObservableObject, IMenuContribution
{
    public string Id => contribution.Id;
    public string MenuPath => contribution.MenuPath;
    public string Title { get; private set; } = contribution.Title;
    public string? ShortcutGesture => contribution.ShortcutGesture;
    public void Execute(IEditorWorkspace workspace) => contribution.Execute(workspace);
    public void RefreshTranslation()
    {
        // Read first so a failing plugin leaves its previous presentation intact.
        try { var title = contribution.Title; Title = title; OnPropertyChanged(nameof(Title)); }
        catch { /* A third-party display getter must not stop host localization. */ }
    }
}

public sealed class PluginToolbarItem(IToolbarContribution contribution) : ObservableObject, IToolbarContribution
{
    public string Id => contribution.Id;
    public string Title { get; private set; } = contribution.Title;
    public string? Tooltip { get; private set; } = contribution.Tooltip;
    public string? IconPathData => contribution.IconPathData;
    public void Execute(IEditorWorkspace workspace) => contribution.Execute(workspace);
    public void RefreshTranslation()
    {
        try
        {
            var title = contribution.Title;
            var tooltip = contribution.Tooltip;
            Title = title;
            Tooltip = tooltip;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Tooltip));
        }
        catch { /* Keep the previous presentation on plugin failure. */ }
    }
}
