using FocusDesk.Helpers;
using FocusDesk.Services;
using FocusDesk.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using ZeroZero.Diagnostics;

namespace FocusDesk;

/// <summary>
/// The application object. Starts the services a session depends on, in the order they depend on
/// each other, puts the icon in the notification area and holds the one off-screen window the XAML
/// runtime needs to own.
/// </summary>
/// <remarks>Nothing on screen but the icon: the status window and the Settings window are opened
/// from it and closing either leaves the application running. Only the menu's Exit ends the
/// process.</remarks>
public partial class App : Application
{
    /// <summary>The plain file every crash arm appends to, beside the log.</summary>
    internal const string CrashLineFileName = "crash.log";

    private Window? _hostWindow;

    // Held for the life of the process: the arms stay registered until it ends.
    private readonly CrashHandlers _crashHandlers;

    public App()
    {
        // Before anything else touches the session state or the tray icon: a second instance —
        // most often the watchdog task's own probe firing while FocusDesk is already running —
        // must not get this far.
        if (!SingleInstanceGuard.TryAcquire())
            Environment.Exit(0);

        // The first line of every run, before anything that can throw and before the crash arms are
        // registered: a log read beside a source tree needs the whole revision to find the commit.
        StartupVersionLine.Write(new AppLogSink(), typeof(App).Assembly);

        InitializeComponent();

        // Before any window exists: without this the dispatcher stops with the last window closed,
        // which would end the process the moment the status window or the Settings window is shut.
        // Leaving is the tray menu's Exit and nothing else.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;

        // AppDomain and unobserved-task arms come from the shared library; the WinUI arm is the
        // application's own and reports through the same sink.
        // The crash line is a plain file beside the log, written with a flush to disk: the logging
        // framework's own buffers are not what a process falling over should depend on.
        _crashHandlers = CrashHandlers.Register(new CrashHandlerOptions
        {
            Sink      = new AppLogSink(),
            CrashLine = new CrashLineAppender(AppPaths.LogFile(CrashLineFileName)),
        });
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
            // The accent keys, before any window is built: a control resolves one once, when it is
            // created. Not from the constructor — Microsoft.UI.Xaml.dll 3.2.3.0 fails
            // Application.Resources with E_UNEXPECTED while the initialisation callback is still
            // running, and the stowed exception ends the process at 0xC000027B before the crash arms
            // can report it.
            AppPalette.Apply(Resources);

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

            // After the session engine and the screen services, whose current values its first
            // announcement reads. Home Assistant is the only way a session is ended early, so this is
            // what makes the application's own description true.
            MqttService.Start();
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

            // Created but never activated: it is what the XAML runtime owns, not something to look
            // at. The icon is what a person sees.
            _hostWindow = new MainWindow();

            // Before the icon, which offers the check from its menu. The sweep of leftover downloads
            // runs here, while no install can be in flight.
            AppUpdates.Start(Shutdown);

            TrayIconHost.Start(Shutdown);
        }
        catch (Exception ex)
        {
            AppLog.Error("OnLaunched", ex);
            throw;
        }
    }

    /// <summary>
    /// Leaving, as chosen from the tray menu. The session itself is untouched — its record stays on
    /// disk and the next start resumes it — but everything holding a resource is let go in order:
    /// the icon out of the shell, the session's own timer and cover, then the process.
    /// </summary>
    private void Shutdown()
    {
        AppLog.Info("Exit was chosen from the notification-area menu.");

        try { TrayIconHost.Stop(); }
        catch (Exception ex) { AppLog.Error("Shutdown.TrayIconHost", ex); }

        try
        {
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            // Before the session engine: the last thing published is the state the session is
            // actually in, and then the device goes offline rather than timing out on its will.
            MqttService.Stop();
        }
        catch (Exception ex) { AppLog.Error("Shutdown.MqttService", ex); }

        try { FocusSessionService.Stop(); }
        catch (Exception ex) { AppLog.Error("Shutdown.FocusSessionService", ex); }

        try { _hostWindow?.Close(); }
        catch (Exception ex) { AppLog.Error("Shutdown.HostWindow", ex); }

        Exit();
    }

    /// <summary>A resume from standby. The broker connection is told so it stops waiting out a
    /// backoff the machine slept through and reconnects at once.</summary>
    /// <remarks>Raised on SystemEvents' own hidden-window thread, and nothing it reaches touches the
    /// UI, so it runs where it arrives.</remarks>
    private static void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;

        try { MqttService.OnPowerResume(); }
        catch (Exception ex) { AppLog.Error("App.OnPowerModeChanged", ex); }
    }
}
