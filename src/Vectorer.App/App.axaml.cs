using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Vectorer.App.Localization;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Vectorer.App.Views;

namespace Vectorer.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var osUiCulture = CultureInfo.CurrentUICulture;
            var formattingCulture = CultureInfo.CurrentCulture;

            var splash = new Window
            {
                Width = 320,
                Height = 140,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                Content = new TextBlock
                {
                    Text = "Vectorer",
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    FontSize = 18
                }
            };
            desktop.MainWindow = splash;
            _ = InitializeMainWindowAsync(desktop, splash, osUiCulture, formattingCulture);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeMainWindowAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        Window splash,
        CultureInfo osUiCulture,
        CultureInfo formattingCulture)
    {
        var store = new JsonEditorUserSettingsStore();
        EditorUserSettings settings;
        try
        {
            settings = await store.LoadAsync();
        }
        catch
        {
            settings = new EditorUserSettings();
        }

        var localization = new AppLocalization(osUiCulture, formattingCulture, settings.UiLanguage);
        CultureInfo.CurrentUICulture = localization.UiCulture;
        CultureInfo.DefaultThreadCurrentUICulture = localization.UiCulture;

        var viewModel = new MainWindowViewModel(localization);
        viewModel.ApplyLoadedSettings(settings, store);

        var mainWindow = new MainWindow
        {
            DataContext = viewModel
        };
        desktop.MainWindow = mainWindow;
        mainWindow.Show();
        splash.Close();
    }
}
