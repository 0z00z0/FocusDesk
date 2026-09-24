using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ZeroZero.Diagnostics;

namespace FocusDesk;

/// <summary>
/// The application object. Starts the services a session depends on, in the order they depend on
/// each other, and owns the window the process runs behind. That window is a stand-in launcher for
/// the Settings shell; the tray icon replaces it with the code it belongs to.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    // Held for the life of the process: the arms stay registered until it ends.
    private readonly CrashHandlers _crashHandlers;

    public App()
    {
        // Before anything else touches the session state or the tray icon: a second instance —
        // most often the watchdog task's own probe firing while FocusDesk is already running —
        // must not get this far.
        if (!SingleInstanceGuard.TryAcquire())
            Environment.Exit(0);

        InitializeComponent();

        // AppDomain and unobserved-task arms come from the shared library; the WinUI arm is the
        // application's own and reports through the same sink.
        _crashHandlers = CrashHandlers.Register(new CrashHandlerOptions { Sink = new AppLogSink() });
        UnhandledException += (_, e) =>
        {
            _crashHandlers.Report("Application.UnhandledException", e.Exception);
            // Leave e.Handled = false: crashing visibly beats running corrupt.
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            AppLog.Info("FocusDesk starting.");

            // General crash-resilience infrastructure, independent of any focus-session lever: a
            // deliberate start re-arms resurrection, and the watchdog task itself is (re)registered
            // unconditionally on every start, whether or not a session is running.
            WatchdogTask.TryClearHoldMarker();
            WatchdogTask.TryEnsureTask();

            // First of the session services: Windows keeps a brightness across a restart, so a
            // level a run that died left displaced stays displaced until this puts it back. A
            // session that is resuming then dims it again and parks the level it found.
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
