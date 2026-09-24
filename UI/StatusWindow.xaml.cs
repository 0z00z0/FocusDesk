using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;

namespace FocusDesk.UI;

/// <summary>
/// What the machine is doing and how to set it going: the session's own line, the start box, and the
/// sessions that have finished. Reached from the notification-area icon, by a click or from its menu.
/// </summary>
/// <remarks>
/// <para>One window at a time — a second request brings the open one forward.</para>
/// <para>Nothing here ends a session. The start box starts one; the way out is Home Assistant's
/// staged cancel and the session's own clock, and the window says so while one runs.</para>
/// </remarks>
internal sealed partial class StatusWindow : Window
{
    /// <summary>How many finished sessions the list shows. Enough to see a day's work behind one
    /// and short enough that the window still opens at its content's height.</summary>
    private const int HistoryRowCount = 6;

    /// <summary>The window's width in device-independent units: the content panel's cap plus the
    /// scroller's padding on both sides.</summary>
    private const int WidthInUnits = 360;

    /// <summary>What the height is held to whatever the content asks for, so a long history cannot
    /// drive the window off a small screen. The scroller takes over past this.</summary>
    private const int MaxHeightInUnits = 640;

    private static StatusWindow? _open;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Opens the status window, or brings the open one forward.</summary>
    public static void Open()
    {
        if (_open is { } already)
        {
            already.Activate();
            return;
        }

        var window = new StatusWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        window.Activate();
    }

    private StatusWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.Name} status";

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        AppWindow.SetPresenter(presenter);
        // The executable's own mark is compiled into it, which a WinUI window does not pick up; the
        // title bar is set from the copy of the same file beside the exe.
        if (File.Exists(AppIcons.Application)) AppWindow.SetIcon(AppIcons.Application);

        // The session moves on its own clock and from Home Assistant, so the window follows both:
        // the engine's event for a stage moving, and a tick for the minutes running down.
        FocusSessionService.Changed += OnSessionChanged;
        _tick.Tick += (_, _) => ShowSession();

        ContentPanel.Loaded += (_, _) =>
        {
            Reload();
            FitToContent();
            _tick.Start();
        };

        Closed += (_, _) =>
        {
            _tick.Stop();
            FocusSessionService.Changed -= OnSessionChanged;
        };
    }

    private void OnSessionChanged() => DispatcherQueue.TryEnqueue(Reload);

    /// <summary>Reads the session, the settings and the history, and brings every part of the window
    /// into line with them.</summary>
    private void Reload()
    {
        ShowSession();
        ShowStartBox();
        ShowHistory();
    }

    /// <summary>The focus row: the session's own line, and the way out beneath it while one runs.</summary>
    private void ShowSession()
    {
        var session = FocusSessionService.Current;

        StatusText.Text = session.IsRunning
            ? FocusSessionStages.Detail(session, DateTimeOffset.Now)
            : "No session is running.";
        WayOutText.Visibility = session.IsRunning ? Visibility.Visible : Visibility.Collapsed;

        // A second session cannot be armed over a running one, so the button says nothing it cannot
        // do. The refusal line from an earlier attempt goes with it.
        StartButton.IsEnabled = !session.IsRunning;
        MinutesCombo.IsEnabled = !session.IsRunning;
        if (session.IsRunning) RefusalText.Visibility = Visibility.Collapsed;
    }

    /// <summary>The start box, and the length it offers. Hidden altogether where the Focus settings
    /// page has not turned it on: a machine that is only ever set going from Home Assistant has no
    /// use for it.</summary>
    private void ShowStartBox()
    {
        var (offered, minutes) = SettingsService.Read(
            s => (s.FocusStartFromDashboard, s.FocusSessionMinutes));

        StartBox.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
        if (!offered) return;

        FocusLengthChoices.Fill(MinutesCombo, minutes);
    }

    /// <summary>The finished sessions, newest first.</summary>
    private void ShowHistory()
    {
        HistoryRows.Children.Clear();

        var recent = FocusHistoryService.Recent(HistoryRowCount);
        if (recent.Count == 0)
        {
            HistoryRows.Children.Add(Line("No session has finished yet.", secondary: true));
            return;
        }

        foreach (var entry in recent)
            HistoryRows.Children.Add(Line(FocusHistoryService.Describe(entry), secondary: false));
    }

    /// <summary>One line of the list. The opacity rather than a theme brush: a brush looked up from
    /// the application's own dictionary does not follow a light or dark switch, and these lines are
    /// built in code rather than in markup where the theme reference would.</summary>
    private static TextBlock Line(string text, bool secondary) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Opacity = secondary ? 0.65 : 1,
    };

    /// <summary>Starts a session for the chosen length. Everything else about it — which levers it
    /// takes — comes from the settings, so this box changes the length and nothing else.</summary>
    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (FocusLengthChoices.Selected(MinutesCombo) is not { } minutes) return;

        // The chosen length is what the next session runs for from wherever it is started, so it is
        // stored rather than held for this one start.
        SettingsService.Update(s => s.FocusSessionMinutes = minutes);

        var outcome = FocusSessionService.Arm(ActionCause.StatusWindow("Start button"), minutes);
        if (outcome == FocusArmOutcome.Armed)
        {
            RefusalText.Visibility = Visibility.Collapsed;
            Reload();
            return;
        }

        RefusalText.Text = Refusal(outcome);
        RefusalText.Visibility = Visibility.Visible;
    }

    /// <summary>Why nothing started, in the words of whoever pressed the button. Every one of these
    /// leaves the machine exactly as it was.</summary>
    private static string Refusal(FocusArmOutcome outcome) => outcome switch
    {
        FocusArmOutcome.AlreadyRunning => "A session is already running.",
        FocusArmOutcome.NoLeverChosen  => "Nothing was chosen for a session to do. Turn on dimming or the screen cover on the Focus settings page first.",
        FocusArmOutcome.LeverRefused   => "The screen would not take it: this display accepts no brightness change, or there is nothing attached to cover.",
        _                              => "Something the session needed failed to engage. Whatever did engage has been lifted again.",
    };

    private void OnSettings(object sender, RoutedEventArgs e) =>
        SettingsShellHost.Open(SettingsShellHost.FocusTag);

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    /// <summary>Sizes the window to what it holds and puts it on the display the pointer is on,
    /// which is the display the notification area it was opened from sits on.</summary>
    private void FitToContent()
    {
        double scale = ContentPanel.XamlRoot?.RasterizationScale ?? 1;

        ContentPanel.Measure(new Size(WidthInUnits, double.PositiveInfinity));
        // The scroller's padding on both sides and the content's own measured height.
        double heightInUnits = Math.Min(MaxHeightInUnits, ContentPanel.DesiredSize.Height + 40);

        AppWindow.ResizeClient(new SizeInt32(
            (int)Math.Round(WidthInUnits * scale),
            (int)Math.Round(heightInUnits * scale)));

        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        AppWindow.Move(new PointInt32(
            area.WorkArea.X + ((area.WorkArea.Width  - AppWindow.Size.Width)  / 2),
            area.WorkArea.Y + ((area.WorkArea.Height - AppWindow.Size.Height) / 2)));
    }
}
