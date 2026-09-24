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
    /// <summary>The lengths offered, in the order they are offered. A duration set from Home
    /// Assistant can be any whole number of minutes, so one that is not here is added to the
    /// list rather than rounded to a neighbour.</summary>
    private static readonly (string Label, int Value)[] MinutesPresets =
    [
        ("15 min", 15), ("25 min", 25), ("45 min", 45), ("1 hour", 60),
        ("90 min", 90), ("2 hours", 120), ("3 hours", 180), ("4 hours", 240),
    ];

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

            LoadMinutes(minutes);

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

    /// <summary>Fills the list and selects the stored length, adding it as a choice of its own where
    /// it is not one of the presets.</summary>
    private void LoadMinutes(int stored)
    {
        FocusMinutesCombo.Items.Clear();

        var offered = MinutesPresets.ToList();
        if (!offered.Any(p => p.Value == stored))
        {
            int at = offered.FindIndex(p => p.Value > stored);
            offered.Insert(at < 0 ? offered.Count : at, ($"{stored} min", stored));
        }

        foreach (var (label, value) in offered)
            FocusMinutesCombo.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        FocusMinutesCombo.SelectedIndex = offered.FindIndex(p => p.Value == stored);
    }

    private void OnFocusMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (FocusMinutesCombo.SelectedItem is not ComboBoxItem { Tag: int minutes }) return;

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
