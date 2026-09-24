using Microsoft.UI.Xaml;
using FocusDesk.Services;

namespace FocusDesk;

/// <summary>
/// The application object. Holds no behaviour yet beyond logging — the tray icon, the session
/// engine and the windows arrive with the code they belong to.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppLog.Info("FocusDesk starting.");

        try
        {
            // Before any window: Windows keeps a brightness across a restart, so a level a run that
            // died left displaced stays displaced until this puts it back.
            ScreenBrightnessService.Start();

            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            AppLog.Error("OnLaunched", ex);
            throw;
        }
    }
}
