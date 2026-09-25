using CommunityToolkit.WinUI.Controls;
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

            var (minutes, network, dims, covers, input, startFromStatus) = SettingsService.Read(
                s => (s.FocusSessionMinutes, s.FocusBlocksNetwork, s.FocusDimsScreen,
                      s.FocusCoversScreen, s.FocusBlocksInput, s.FocusStartFromDashboard));

            FocusLengthChoices.Fill(FocusMinutesCombo, minutes);

            // A running session reports the levers it actually holds — a refused input block reads
            // off — and with none running these show the defaults the next session starts from.
            FocusBlocksNetworkToggle.IsOn = session.IsRunning ? session.BlocksNetwork : network;
            FocusDimsScreenToggle.IsOn   = session.IsRunning ? session.DimsScreen   : dims;
            FocusCoversScreenToggle.IsOn = session.IsRunning ? session.CoversScreen : covers;
            FocusBlocksInputToggle.IsOn  = session.IsRunning ? session.BlocksInput  : input;
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

    /// <summary>One row per allowed program: its own name, its path beneath, and a way to take it
    /// off. Locked for the length of a session, like the lever switches.</summary>
    private void ShowAllowedPrograms(bool locked)
    {
        FocusAllowedProgramsPanel.Children.Clear();

        foreach (var entry in SettingsService.Read(s => s.FocusPrograms.ToList()))
        {
            var remove = new Button { Content = "Remove", IsEnabled = !locked };
            var program = entry;
            remove.Click += (_, _) => ChangeAllowedPrograms(
                list => FocusAllowedPrograms.Remove(list, program.Kind, program.Id,
                                                    FocusSessionService.LeversAreLocked));

            FocusAllowedProgramsPanel.Children.Add(new SettingsCard
            {
                Header      = FocusAllowedPrograms.DisplayName(entry),
                Description = FocusAllowedPrograms.Describe(entry),
                Content     = remove,
            });
        }
    }

    /// <summary>A program chosen in the picker, as a row starts: both checkboxes ticked.</summary>
    private static FocusProgramEntry Chosen(string path) => new()
    {
        Kind          = FocusProgramKind.ProgramFile,
        Id            = path,
        CanRun        = true,
        CanUseNetwork = true,
    };

    /// <summary>Opens the picker and puts what comes back on the list. The picker offers what is open
    /// now and what the Start menu holds, and keeps a file dialog for a program in neither.</summary>
    private void OnFocusAllowProgram(object sender, RoutedEventArgs e)
    {
        try
        {
            new ProgramPickerWindow(chosen => DispatcherQueue.TryEnqueue(() => ChangeAllowedPrograms(
                list => FocusAllowedPrograms.Add(list, Chosen(chosen), FocusSessionService.LeversAreLocked))))
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
        FocusAllowVerdict.SessionRunning => "The list cannot change while a session is running.",
        FocusAllowVerdict.NotAProgram    => "That file is not a program a firewall rule can name.",
        FocusAllowVerdict.ListFull       => $"The list already holds {FocusAllowedPrograms.Maximum} programs, which is as many as it takes.",
        FocusAllowVerdict.AlreadyAllowed => "That program is already on the list.",
        FocusAllowVerdict.NotAllowed     => "That program was not on the list.",
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

    private void OnFocusStartFromStatusToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        SettingsService.Update(s => s.FocusStartFromDashboard = FocusStartFromStatusToggle.IsOn);
    }
}
