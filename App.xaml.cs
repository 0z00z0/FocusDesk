using FocusDesk.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FocusDesk;

/// <summary>
/// The application object. Starts the services a session depends on, in the order they depend on
/// each other, and owns the one window there is — the tray icon and the Settings shell arrive with
/// the code they belong to.
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
            // First: Windows keeps a brightness across a restart, so a level a run that died left
            // displaced stays displaced until this puts it back. A session that is resuming then
            // dims it again and parks the level it found.
            ScreenBrightnessService.Start();

            // Before the session engine, which can ask for a cover the moment it resumes one.
            ScreenCoverService.Start(DispatcherQueue.GetForCurrentThread(), () => FocusSessionService.Current);

            FocusSessionService.Start();

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
