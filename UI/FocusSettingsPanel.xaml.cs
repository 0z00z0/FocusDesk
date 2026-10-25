using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The Focus session page: what the session is doing, how long and of which kind the next one is,
/// the focus point, the setup guide for an automatic session and the published way out. Nothing
/// here ends a session — no control on this machine does.
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
        PageTitle.Text = AppText.Get("SettingsFocusSessionPage");
        SessionHeader.Heading = AppText.Get("FocusSessionSectionHeading");
        FocusStatusCard.Header = AppText.Get("FocusStatusHeader");
        FocusStatusCard.Description = AppText.Get("FocusStatusDescription");
        FocusLengthCard.Header = AppText.Get("FocusLengthHeader");
        FocusLengthCard.Description = AppText.Get("FocusLengthDescription");
        FocusGraceCard.Header = AppText.Get("FocusGraceHeader");
        FocusGraceCard.Description = AppText.Get("FocusGraceDescription");
        AutomaticSessionHeader.Heading = AppText.Get("AutomaticSessionSectionLabel");
        GuideCard.Header = AppText.Get("AutomaticSessionGuideHeader");
        GuideCard.Description = AppText.Get("AutomaticSessionGuideDescription");
        OpenGuideButton.Content = AppText.Get("AutomaticSessionGuideButton");
        StuckHeader.Heading = AppText.Get("FocusStuckHeading");
        StuckEndEarlyText.Text = AppText.Get("FocusStuckEndEarly");
        StuckNetworkText.Text = AppText.Get("FocusStuckNetwork");
        StuckRulesText.Text = AppText.Format("FocusStuckRules", FocusFirewallRules.Group, FocusFirewallRules.BrokerRuleName);
        StuckInputText.Text = AppText.Get("FocusStuckInput");
        StuckBrightnessText.Text = AppText.Get("FocusStuckBrightness");
        FocusKindCard.Header      = AppText.Get("FocusKindCardHeader");
        FocusKindCard.Description = AppText.Get("FocusKindCardDescription");
        FocusKindProgramFocusRadio.Content = AppText.Get("FocusKindProgramFocusOption");
        FocusKindScreenBreakRadio.Content  = AppText.Get("FocusKindScreenBreakOption");
        FocusPointHeader.Heading = AppText.Get("FocusPointHeading");
        FocusPointShownCard.Header      = AppText.Get("FocusPointShownHeader");
        FocusPointShownCard.Description = AppText.Get("FocusPointShownDescription");
        FocusPointStartsSessionCard.Header      = AppText.Get("FocusPointStartsSessionHeader");
        FocusPointStartsSessionCard.Description = AppText.Get("FocusPointStartsSessionDescription");
        FocusPointSecondsCard.Header      = AppText.Get("FocusPointSecondsHeader");
        FocusPointSecondsCard.Description = AppText.Get("FocusPointSecondsDescription");
        FocusPointSecondsBox.Minimum = FocusPointStart.MinSeconds;
        FocusPointSecondsBox.Maximum = FocusPointStart.MaxSeconds;
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
            FocusStatusValue.Text = FocusSessionStages.Describe(session, DateTimeOffset.Now);

            var (minutes, kind) = SettingsService.Read(
                s => (s.FocusSessionMinutes, s.FocusSessionKind));

            FocusLengthChoices.Fill(FocusMinutesCombo, minutes);

            FocusGraceCombo.Items.Clear();
            foreach (int seconds in SettingsFile.FocusGraceChoices)
                FocusGraceCombo.Items.Add(new ComboBoxItem { Content = AppText.Get($"FocusGraceChoice{seconds}"), Tag = seconds });
            FocusGraceCombo.SelectedIndex = Array.IndexOf(SettingsFile.FocusGraceChoices,
                SettingsService.Read(s => s.FocusGraceSeconds));
            FocusGraceCombo.IsEnabled = !locked;

            // A running session shows the kind it was started as, which may not be the stored one:
            // the status window starts either kind without storing it.
            var shownKind = session.IsRunning ? session.Kind : kind;
            FocusKindProgramFocusRadio.IsChecked = shownKind == FocusSessionKind.ProgramFocus;
            FocusKindScreenBreakRadio.IsChecked  = shownKind == FocusSessionKind.ScreenBreak;
            FocusKindProgramFocusRadio.IsEnabled = !locked;
            FocusKindScreenBreakRadio.IsEnabled  = !locked;

            var (pointShown, pointStarts, pointSeconds) = SettingsService.Read(
                s => (s.FocusPointShown, s.FocusPointStartsSession, s.FocusPointSeconds));
            FocusPointShownToggle.IsOn         = pointShown;
            FocusPointStartsSessionToggle.IsOn = pointStarts;
            FocusPointSecondsBox.Value         = pointSeconds;
            // Both describe a focus point that is not shown otherwise.
            FocusPointStartsSessionToggle.IsEnabled = pointShown;
            FocusPointSecondsBox.IsEnabled          = pointShown;
        }
        finally { _updating = false; }
    }

    private void OnFocusMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (FocusLengthChoices.Selected(FocusMinutesCombo) is not { } minutes) return;

        SettingsService.Update(s => s.FocusSessionMinutes = minutes);
    }

    private void OnFocusGraceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (FocusGraceCombo.SelectedItem is not ComboBoxItem { Tag: int seconds }) return;
        // Checked at the moment of the write, as the kind is.
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }

        SettingsService.Update(s => s.FocusGraceSeconds = seconds);
    }

    private void OnFocusKindChecked(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        // Checked at the moment of the write, as the list is: a kind changed while a session runs would
        // leave the page and the running session disagreeing.
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        var kind = ReferenceEquals(sender, FocusKindProgramFocusRadio)
            ? FocusSessionKind.ProgramFocus
            : FocusSessionKind.ScreenBreak;
        SettingsService.Update(s => s.FocusSessionKind = kind);
    }

    private void OnFocusPointShownToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusPointShown = FocusPointShownToggle.IsOn);
        Reload();
    }

    private void OnFocusPointStartsSessionToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusPointStartsSession = FocusPointStartsSessionToggle.IsOn);
    }

    /// <summary>Stores a whole number of seconds within the bounds. A cleared box puts the stored value
    /// back rather than storing nothing.</summary>
    private void OnFocusPointSecondsChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_updating) return;
        if (double.IsNaN(args.NewValue)) { Reload(); return; }

        int seconds = (int)Math.Round(Math.Clamp(args.NewValue, FocusPointStart.MinSeconds, FocusPointStart.MaxSeconds));
        SettingsService.Update(s => s.FocusPointSeconds = seconds);
    }

    private void OnOpenGuideClick(object sender, RoutedEventArgs e) => SetupGuideWindow.Open();
}
