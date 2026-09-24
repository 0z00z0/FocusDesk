using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The Focus page: what the session is doing, how long the next one runs, which levers it takes, and
/// the published way out. Nothing here ends a session — no control on this machine does.
/// </summary>
/// <remarks>The session moves on its own clock and from Home Assistant, so the page follows
/// <see cref="FocusSessionService.Changed"/> while it is on screen and stops following it when it
/// leaves.</remarks>
public sealed partial class FocusSettingsPanel : UserControl
{
    private bool _updating;
    private bool _watching;

    public FocusSettingsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    /// <summary>Follows the session while the page is on screen. The engine's tick runs on a timer
    /// thread, so every reading is marshalled back to the thread that draws.</summary>
    public void Watch()
    {
        Reload();
        if (_watching) return;
        FocusSessionService.Changed += OnSessionChanged;
        _watching = true;
    }

    /// <summary>Stops following the session. A page nobody is looking at redrawing once a second
    /// costs a settings read and a layout pass for nothing.</summary>
    public void Unwatch()
    {
        if (!_watching) return;
        FocusSessionService.Changed -= OnSessionChanged;
        _watching = false;
    }

    private void OnSessionChanged() => DispatcherQueue.TryEnqueue(Reload);

    /// <summary>Reads the settings and the session and brings every control into line with them.</summary>
    public void Reload()
    {
        var session = FocusSessionService.Current;
        bool locked = FocusSessionService.LeversAreLocked;

        _updating = true;
        try
        {
            FocusStatusValue.Text = FocusSessionStages.Detail(session, DateTimeOffset.Now);

            var (minutes, dims, covers, startFromStatus) = SettingsService.Read(
                s => (s.FocusSessionMinutes, s.FocusDimsScreen, s.FocusCoversScreen,
                      s.FocusStartFromDashboard));

            FocusLengthChoices.Fill(FocusMinutesCombo, minutes);

            // A running session reports the levers it actually owns; with none running the two show
            // the defaults the next session would start from.
            FocusDimsScreenToggle.IsOn   = session.IsRunning ? session.DimsScreen   : dims;
            FocusCoversScreenToggle.IsOn = session.IsRunning ? session.CoversScreen : covers;
            FocusDimsScreenToggle.IsEnabled   = !locked;
            FocusCoversScreenToggle.IsEnabled = !locked;

            FocusStartFromStatusToggle.IsOn = startFromStatus;
        }
        finally { _updating = false; }
    }

    private void OnFocusMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (FocusLengthChoices.Selected(FocusMinutesCombo) is not { } minutes) return;

        SettingsService.Update(s => s.FocusSessionMinutes = minutes);
    }

    private void OnFocusDimsScreenToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusDimsScreen = FocusDimsScreenToggle.IsOn);
    }

    private void OnFocusCoversScreenToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusCoversScreen = FocusCoversScreenToggle.IsOn);
    }

    private void OnFocusStartFromStatusToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusStartFromDashboard = FocusStartFromStatusToggle.IsOn);
    }
}
