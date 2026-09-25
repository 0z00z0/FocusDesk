using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The Focus page: what the session is doing, how long the next one runs, which levers it takes, the
/// programs that keep the network, and the published way out. Nothing here ends a session — no
/// control on this machine does.
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
        FocusProgramsCard.Header      = AppText.Get("FocusProgramsHeader");
        FocusProgramsCard.Description = AppText.Get("FocusProgramsDescription");
        FocusAllowProgramButton.Content = AppText.Get("FocusProgramsAddButton");
        FocusLimitsProgramsCard.Header      = AppText.Get("FocusLimitsProgramsHeader");
        FocusLimitsProgramsCard.Description = AppText.Get("FocusLimitsProgramsDescription");
        FocusLimitsProgramsInfo.Subject = AppText.Get("FocusLimitsProgramsSubject");
        FocusLimitsProgramsInfo.Info    = AppText.Get("FocusLimitsProgramsInfo");
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

            var (minutes, network, dims, covers, input, startFromStatus, programs) = SettingsService.Read(
                s => (s.FocusSessionMinutes, s.FocusBlocksNetwork, s.FocusDimsScreen,
                      s.FocusCoversScreen, s.FocusBlocksInput, s.FocusStartFromDashboard,
                      s.FocusLimitsPrograms));

            FocusLengthChoices.Fill(FocusMinutesCombo, minutes);

            // A running session reports the levers it actually holds — a refused input block reads
            // off — and with none running these show the defaults the next session starts from.
            FocusBlocksNetworkToggle.IsOn = session.IsRunning ? session.BlocksNetwork : network;
            FocusDimsScreenToggle.IsOn   = session.IsRunning ? session.DimsScreen   : dims;
            FocusCoversScreenToggle.IsOn = session.IsRunning ? session.CoversScreen : covers;
            FocusBlocksInputToggle.IsOn  = session.IsRunning ? session.BlocksInput  : input;
            FocusLimitsProgramsToggle.IsOn = session.IsRunning ? session.LimitsPrograms : programs;
            FocusLimitsProgramsToggle.IsEnabled = !locked;
            FocusBlocksNetworkToggle.IsEnabled = !locked;
            FocusAllowProgramButton.IsEnabled  = !locked;
            FocusDimsScreenToggle.IsEnabled   = !locked;
            FocusCoversScreenToggle.IsEnabled = !locked;
            FocusBlocksInputToggle.IsEnabled  = !locked;

            FocusStartFromStatusToggle.IsOn = startFromStatus;

            ShowAllowedPrograms(locked);
        }
        finally { _updating = false; }
    }

    /// <summary>One row per program: its icon and name, what it is beneath, "can run", "can use the
    /// network" and a way to take it off. A checkbox that does nothing for that kind of program is
    /// shown disabled, with the reason on hover. Locked for the length of a session, like the lever
    /// switches.</summary>
    private void ShowAllowedPrograms(bool locked)
    {
        FocusAllowedProgramsPanel.Children.Clear();
        string windows = WindowsPrograms.Folder;

        foreach (var entry in SettingsService.Read(s => s.FocusPrograms.ToList()))
        {
            var program = entry;
            var offer = ProgramCatalogue.OfferFor(entry, windows);

            var canRun = new CheckBox
            {
                Content   = AppText.Get("FocusProgramCanRun"),
                IsChecked = offer == ProgramOffer.NetworkOnly || entry.CanRun,
                IsEnabled = !locked && offer != ProgramOffer.NetworkOnly,
            };
            var canUseNetwork = new CheckBox
            {
                Content   = AppText.Get("FocusProgramCanUseNetwork"),
                IsChecked = offer != ProgramOffer.RunOnly && entry.CanUseNetwork,
                IsEnabled = !locked && offer != ProgramOffer.RunOnly,
            };
            // Wired after the initial state is set, so drawing the row changes nothing.
            canRun.Click += (_, _) => ChangeAllowedPrograms(list => FocusAllowedPrograms.SetCanRun(
                list, program.Kind, program.Id, canRun.IsChecked == true, FocusSessionService.LeversAreLocked));
            canUseNetwork.Click += (_, _) => ChangeAllowedPrograms(list => FocusAllowedPrograms.SetCanUseNetwork(
                list, program.Kind, program.Id, canUseNetwork.IsChecked == true, FocusSessionService.LeversAreLocked));

            var remove = new Button { Content = AppText.Get("FocusProgramRemove"), IsEnabled = !locked };
            remove.Click += (_, _) => ChangeAllowedPrograms(
                list => FocusAllowedPrograms.Remove(list, program.Kind, program.Id,
                                                    FocusSessionService.LeversAreLocked));

            string? runWhy = offer == ProgramOffer.NetworkOnly ? AppText.Get("FocusProgramWindowsRunInfo") : null;
            string? networkWhy = offer != ProgramOffer.RunOnly ? null
                : AppText.Get(entry.Kind == FocusProgramKind.WebApp
                                  ? "FocusProgramWebAppNetworkInfo"
                                  : "FocusProgramStoreNetworkInfo");

            var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            controls.Children.Add(WithReason(canRun, runWhy));
            controls.Children.Add(WithReason(canUseNetwork, networkWhy));
            controls.Children.Add(remove);

            var icon = new ImageIcon { Width = 24, Height = 24 };
            ProgramIconLoader.Request(entry.StartEntry ?? entry.Id, image => icon.Source = image);

            FocusAllowedProgramsPanel.Children.Add(new SettingsCard
            {
                HeaderIcon  = icon,
                Header      = ProgramNames.For(entry),
                Description = ProgramCatalogue.IsPresent(entry)
                    ? FocusAllowedPrograms.Describe(entry)
                    : AppText.Get("FocusProgramNotFound"),
                Content     = controls,
            });
        }
    }

    /// <summary>A disabled control shows no hover text of its own, so the reason sits on a
    /// transparent frame around it.</summary>
    private static FrameworkElement WithReason(Control control, string? reason)
    {
        if (reason is null) return control;
        var frame = new Grid { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        frame.Children.Add(control);
        ToolTipService.SetToolTip(frame, reason);
        return frame;
    }

    /// <summary>Opens the picker and puts what comes back on the list, both checkboxes ticked. The
    /// picker offers the Start menu's own programs, Store apps and web apps included.</summary>
    private void OnFocusAllowProgram(object sender, RoutedEventArgs e)
    {
        try
        {
            new ProgramPickerWindow(chosen => DispatcherQueue.TryEnqueue(() => ChangeAllowedPrograms(
                list => FocusAllowedPrograms.Add(list, chosen.ToEntry(), FocusSessionService.LeversAreLocked))))
                .Activate();
        }
        catch (Exception ex) { AppLog.Error("FocusSettingsPanel.OnFocusAllowProgram", ex); }
    }

    /// <summary>Runs one change against a copy and writes the list back only where it moved, so a
    /// refusal leaves the document untouched and says why.</summary>
    private void ChangeAllowedPrograms(Func<IList<FocusProgramEntry>, FocusAllowVerdict> change)
    {
        var list = SettingsService.Read(s => s.FocusPrograms.ToList());
        var verdict = change(list);

        if (verdict is FocusAllowVerdict.Added or FocusAllowVerdict.Removed or FocusAllowVerdict.Changed)
            SettingsService.Update(s => s.FocusPrograms = list);

        FocusAllowRefusalText.Text = Refusal(verdict);
        FocusAllowRefusalText.Visibility = FocusAllowRefusalText.Text.Length > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        Reload();
    }

    /// <summary>Why the list did not move, in the words of whoever pressed the button. Nothing for a
    /// change that landed.</summary>
    private static string Refusal(FocusAllowVerdict verdict) => verdict switch
    {
        FocusAllowVerdict.SessionRunning => AppText.Get("FocusProgramsRefusalSessionRunning"),
        FocusAllowVerdict.NotAProgram    => AppText.Get("FocusProgramsRefusalNotAProgram"),
        FocusAllowVerdict.ListFull       => AppText.Format("FocusProgramsRefusalListFull", FocusAllowedPrograms.Maximum),
        FocusAllowVerdict.AlreadyAllowed => AppText.Get("FocusProgramsRefusalAlreadyAllowed"),
        FocusAllowVerdict.NotAllowed     => AppText.Get("FocusProgramsRefusalNotAllowed"),
        FocusAllowVerdict.NotOffered     => AppText.Get("FocusProgramsRefusalNotOffered"),
        _                                => "",
    };

    private void OnFocusMinutesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating) return;
        if (FocusLengthChoices.Selected(FocusMinutesCombo) is not { } minutes) return;

        SettingsService.Update(s => s.FocusSessionMinutes = minutes);
    }

    private void OnFocusBlocksNetworkToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusBlocksNetwork = FocusBlocksNetworkToggle.IsOn);
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

    private void OnFocusBlocksInputToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusBlocksInput = FocusBlocksInputToggle.IsOn);
    }

    private void OnFocusLimitsProgramsToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        // Checked at the moment of the write, as the list is: a switch moved while a session runs would
        // leave the running session and its record disagreeing.
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        SettingsService.Update(s => s.FocusLimitsPrograms = FocusLimitsProgramsToggle.IsOn);
    }

    private void OnFocusStartFromStatusToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusStartFromDashboard = FocusStartFromStatusToggle.IsOn);
    }
}
