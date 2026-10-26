using System.Globalization;
using System.Runtime.InteropServices;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using ZeroZero.Brand.Core;
using ZeroZero.Controls.WinUI;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>
/// The tray pop-out: what the machine is doing and how to set it going. A countdown ring with the
/// session's own lines beside it, the start box, what the session holds, and the sessions that have
/// finished. Opened by a click on the notification-area icon or from its menu.
/// </summary>
/// <remarks>
/// <para>One window at a time. Clicking away or pressing Escape hides it rather than closing it, so
/// the next click re-shows the same window; a long idle spell destroys it and the click after that
/// builds it again.</para>
/// <para>Nothing here ends a session but Cancel, shown only in the grace period after a fresh start.
/// The start box starts one: the two kind buttons only choose,
/// and the Start button opens the focus-point window with the chosen kind and length, which arms the
/// session after its time, or arms it at once where the Focus page turns the focus point off. The way out is Home Assistant's staged cancel and the
/// session's own clock, and the window says so while one runs.</para>
/// </remarks>
internal sealed partial class StatusWindow : Window
{
    /// <summary>How many finished sessions the list shows. Five rows of two lines each keep the
    /// whole pop-out under <see cref="PopOutPlacement.MaxHeightInUnits"/>, so it fits a laptop's
    /// work area without scrolling.</summary>
    private const int HistoryRowCount = 5;

    /// <summary>The ring's geometry inside its 84-unit square.</summary>
    private const double RingCentre = 42;
    private const double RingRadius = 35;

    /// <summary>How strongly an active card is tinted, out of 255. An alpha over whatever sits
    /// behind rather than a blended solid, so it composites correctly over the backdrop.</summary>
    private const byte ActiveTintAlpha = 20;

    /// <summary>How long the window stays hidden before it is destroyed and its composition
    /// resources released. A pop-out opened once in the morning should not hold a XAML tree all
    /// day.</summary>
    private static readonly TimeSpan IdleCloseAfter = TimeSpan.FromMinutes(20);

    private static StatusWindow? _open;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleClose = new() { Interval = IdleCloseAfter };
    private readonly SolidColorBrush _ringFill = new();
    private readonly SolidColorBrush _activeTint = ActiveTint();

    /// <summary>What the history list was last built from, so a tick that changed nothing does not
    /// rebuild rows under the pointer.</summary>
    private string? _historyShown;

    /// <summary>The accent style the markup gives the screen-break button, kept so it can move to
    /// whichever kind is chosen.</summary>
    private readonly Style _accentStyle;

    /// <summary>The kind the Start button hands the focus-point window. Set to the stored default each time the window is
    /// shown, and changed only by the two kind buttons; never written back to the settings.</summary>
    private FocusSessionKind _chosenKind;

    /// <summary>The named session the Start button hands on for Program focus; each showing begins on
    /// the session in use, and pressing a button makes that one the session in use.</summary>
    private string? _chosenSession;

    /// <summary>The mode picked from the Start button's menu for this start only; null leaves each
    /// session on its saved mode. Cleared each time the window is shown.</summary>
    private ProgramFocusMode? _chosenMode;

    private readonly Dictionary<string, ProgramFocusMode> _savedModes = [];
    private bool _sessionsFull;
    private bool _saving;

    // The named-session buttons, copied from ChargeKeeper's Smart Charge preset buttons (5727ea5).
    // Rebuilt only when the set changes, so a tick never replaces a button under the pointer.
    private IReadOnlyList<string> _sessionButtonsShown = [];
    private (int Columns, int Rows) _sessionGridShape;
    private readonly HashSet<string> _runnableSessions = [];

    // The width the row gets before its first arrange: the window less the root's and the card's padding.
    private const double SessionPanelWidth = PopOutPlacement.WidthInUnits - 32 - 22;

    /// <summary>Opens the pop-out, or brings the hidden one back.</summary>
    public static void Open()
    {
        if (_open is { } already)
        {
            already.ShowPopOut();
            return;
        }

        var window = new StatusWindow();
        _open = window;
        window.Closed += (_, _) => _open = null;
        window.ShowPopOut();
    }

    /// <summary>Opens the pop-out with the reason a start from the focus-point window armed
    /// nothing, since that window has already closed.</summary>
    public static void OpenWithRefusal(FocusArmOutcome outcome)
    {
        Open();
        if (_open is not { } window) return;
        window.RefusalText.Text = Refusal(outcome);
        window.RefusalText.Visibility = Visibility.Visible;
    }

    private StatusWindow()
    {
        InitializeComponent();
        Title = AppText.Format("PopOutWindowTitle", AppInfo.Name);

        ConfigureChrome();

        string settings = AppText.Get("PopOutSettings");
        ToolTipService.SetToolTip(SettingsButton, settings);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SettingsButton, settings);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MinutesCombo, AppText.Get("PopOutSessionLength"));
        SessionHeading.Text = AppText.Get("PopOutSessionHeading");
        WayOutText.Text     = AppText.Get("PopOutWayOut");
        LeverHeading.Text   = AppText.Get("PopOutRestrictionsHeading");
        HistoryHeading.Text = AppText.Get("PopOutHistoryHeading");

        RingTrack.Data = RingGeometry.Arc(
            RingCentre, RingCentre, RingRadius, RingGeometry.StartAngle, RingGeometry.Sweep);
        RingFill.Stroke = _ringFill;

        _accentStyle = ScreenBreakChoice.Style;
        ProgramFocusChoice.Content = AppText.Get("PopOutStartProgramFocus");
        ScreenBreakChoice.Content  = AppText.Get("PopOutStartScreenBreak");
        StartButton.Content        = AppText.Get("PopOutStart");
        SaveOpenProgramsButton.Content = AppText.Get("PopOutSaveOpenPrograms");
        SavePositionsButton.Content = AppText.Get("SavePositionsButton");
        ToolTipService.SetToolTip(SavePositionsButton, AppText.Get("SavePositionsInfo"));
        foreach (var mode in ProgramFocusText.Modes)
        {
            var item = new RadioMenuFlyoutItem { Text = ProgramFocusText.ModeName(mode), Tag = mode, GroupName = "Mode" };
            item.Click += OnChooseMode;
            ModeMenu.Items.Add(item);
        }
        // The menu part is a template part, reachable only once the template is applied.
        StartButton.Loaded += (_, _) => ShowChosenKind();
        GraceModeHeading.Text = AppText.Get("PopOutGraceModeHeading");
        foreach (var mode in ProgramFocusText.Modes)
        {
            var choice = new RadioButton { Content = ProgramFocusText.ModeName(mode), Tag = mode, GroupName = "GraceMode", FontSize = 12 };
            choice.Click += OnGraceMode;
            GraceModeChoices.Children.Add(choice);
        }
        GoalBox.PlaceholderText    = AppText.Get("PopOutGoalPlaceholder");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(GoalBox, GoalBox.PlaceholderText);
        GoalBox.MaxLength          = FocusSessionGoal.MaxLength;

        // The session moves on its own clock and from Home Assistant, so the window follows both:
        // the engine's event for a stage moving, and a tick for the minutes running down.
        FocusSessionService.Changed += OnSessionChanged;
        _tick.Tick += (_, _) => ShowSession();
        _idleClose.Tick += (_, _) => CloseIfIdle();

        // A line that appears while the window is open — the way out once a session starts, a
        // refusal — changes the content's height, and the window follows it rather than clipping.
        ContentPanel.SizeChanged += (_, _) => { if (AppWindow.IsVisible) Place(); };
        NamedSessionChoices.SizeChanged += (_, _) => LayoutSessionButtons();

        Activated += OnActivated;

        Closed += (_, _) =>
        {
            _tick.Stop();
            _idleClose.Stop();
            FocusSessionService.Changed -= OnSessionChanged;
        };
    }

    /// <summary>A frameless, always-on-top popup rather than a window with a caption: the frame is
    /// what makes a pop-out read as a pop-out rather than as a window that opened itself.</summary>
    private void ConfigureChrome()
    {
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop  = true;
        AppWindow.SetPresenter(presenter);

        // The presenter keeps a dialog frame with the border turned off: three pixels on every side.
        NativeMethods.RemoveFrame(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

        // Off the taskbar and out of Alt-Tab: a pop-out is not a window to switch to.
        try { SwitcherVisibility.TrySet(AppWindow, shown: false, new AppLogSink()); }
        catch (COMException ex) { AppLog.Error("StatusWindow.IsShownInSwitchers", ex); }
    }

    /// <summary>Reads everything the window shows, places it, and brings it forward.</summary>
    private void ShowPopOut()
    {
        _idleClose.Stop();
        (_chosenKind, _chosenSession) = SettingsService.Read(s => (s.FocusSessionKind, NamedSessions.InUse(s).Key));
        _chosenMode = null;
        GoalBox.Text = "";
        RefusalText.Visibility = Visibility.Collapsed;
        Reload();
        Place();
        AppWindow.Show();
        Activate();
        _tick.Start();
    }

    /// <summary>Hides the window so a re-show is cheap, and tells the tray host, which is what stops
    /// the click that dismissed it opening it again on the mouse-up.</summary>
    private void Dismiss()
    {
        if (!AppWindow.IsVisible) return;

        TrayIconHost.NotePopOutDismissed();
        _tick.Stop();
        AppWindow.Hide();
        // Restarted, so each hide gets a full idle spell measured from itself.
        _idleClose.Stop();
        _idleClose.Start();
    }

    /// <summary>Destroys the window after a long idle spell rather than holding its XAML tree and its
    /// composition resources for the rest of the run. Reclaiming memory must never be able to take
    /// the application down with it.</summary>
    private void CloseIfIdle()
    {
        _idleClose.Stop();
        try
        {
            if (AppWindow.IsVisible) return;
            Close();
        }
        catch (Exception ex) { AppLog.Error("StatusWindow.CloseIfIdle", ex); }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated) Dismiss();
    }

    private void OnSessionChanged() => DispatcherQueue.TryEnqueue(Reload);

    /// <summary>Reads the session, the settings and the history, and brings every part of the window
    /// into line with them.</summary>
    private void Reload()
    {
        ShowSession();
        ShowStartBox();
        ShowLevers();
        ShowHistory();
    }

    /// <summary>The hero: the ring, the minutes at its centre, the session's own line, and the way
    /// out beneath it while one runs.</summary>
    private void ShowSession()
    {
        var session = FocusSessionService.Current;
        var reading = FocusCoverCountdown.For(session, DateTimeOffset.Now);

        if (reading is { } r)
        {
            RingFill.Data = RingGeometry.Arc(
                RingCentre, RingCentre, RingRadius,
                RingGeometry.StartAngle, RingGeometry.Sweep * r.FractionLeft);
            _ringFill.Color  = AppColors.FromPacked(r.Argb);
            MinutesText.Text = r.MinutesLeft.ToString(CultureInfo.CurrentCulture);
        }
        else
        {
            RingFill.Data = null;
            MinutesText.Text = "–";
        }

        StatusText.Text = FocusSessionStages.Describe(session, DateTimeOffset.Now);
        var graceLeft = session.GraceLeft(DateTimeOffset.Now);
        bool inGrace = graceLeft is not null;
        WayOutText.Visibility = session.IsRunning && !inGrace ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility  = inGrace ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = inGrace ? Visibility.Visible : Visibility.Collapsed;
        if (graceLeft is { } left)
        {
            int seconds = (int)Math.Ceiling(left.TotalSeconds);
            CancelButton.Content = AppText.Format("PopOutCancel",
                string.Format(CultureInfo.CurrentCulture, "{0}:{1:00}", seconds / 60, seconds % 60));
        }
        bool modeOpen = inGrace && session.Kind == FocusSessionKind.ProgramFocus;
        GraceModePanel.Visibility = modeOpen ? Visibility.Visible : Visibility.Collapsed;
        if (modeOpen)
            foreach (var choice in GraceModeChoices.Children.OfType<RadioButton>())
                choice.IsChecked = (ProgramFocusMode)choice.Tag == session.Mode;

        // A second session cannot be armed over a running one, so the buttons say nothing they cannot
        // do. The refusal line from an earlier attempt goes with them.
        ProgramFocusChoice.IsEnabled = !session.IsRunning && _programFocusPossible;
        ScreenBreakChoice.IsEnabled  = !session.IsRunning;
        StartButton.IsEnabled        = !session.IsRunning;
        MinutesCombo.IsEnabled       = !session.IsRunning;
        GoalBox.IsEnabled            = !session.IsRunning;
        SaveOpenProgramsButton.IsEnabled = !session.IsRunning && !_sessionsFull && !_saving;
        bool positions = session.IsRunning && session.Kind == FocusSessionKind.ProgramFocus;
        SavePositionsButton.Visibility = positions ? Visibility.Visible : Visibility.Collapsed;
        SavePositionsButton.IsEnabled = !_savingPositions;
        if (!positions) SavePositionsResult.Visibility = Visibility.Collapsed;
        foreach (var button in SessionButtons())
            button.IsEnabled = !session.IsRunning && button.Tag is string key && _runnableSessions.Contains(key);
        if (session.IsRunning && !inGrace) RefusalText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Ends the session inside its grace period; the engine refuses it after.</summary>
    private void OnCancel(object sender, RoutedEventArgs e)
    {
        FocusSessionService.CancelInGrace(ActionCause.StatusWindow("Cancel button"));
        Reload();
    }

    /// <summary>Changes the running Program focus session's mode inside its grace period.</summary>
    private void OnGraceMode(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: ProgramFocusMode mode }) return;
        string? refusal = FocusSessionService.ChangeMode(mode, ActionCause.StatusWindow("mode choice")) switch
        {
            FocusGraceChange.LeverRefused  => "PopOutRefusalLeverRefused",
            FocusGraceChange.LeverFailed   => "PopOutRefusalLeverFailed",
            FocusGraceChange.NothingToOpen => "PopOutRefusalNothingToOpen",
            _                              => null,
        };
        RefusalText.Visibility = refusal is null ? Visibility.Collapsed : Visibility.Visible;
        if (refusal is not null) RefusalText.Text = AppText.Get(refusal);
        Reload();
    }

    /// <summary>Whether any named session can run in some mode, read with the start box. Program focus
    /// is refused without one, so its button says so beforehand.</summary>
    private bool _programFocusPossible;

    /// <summary>The start box: the length, the Start button, and a button per kind with the chosen one
    /// in the accent.</summary>
    private void ShowStartBox()
    {
        var (minutes, stored, count) = SettingsService.Read(
            s => (s.FocusSessionMinutes,
                  s.NamedSessions.Take(NamedSessions.Maximum).Select(n => (n.Key, n.Name, n.Mode, Programs: n.Programs.ToList())).ToList(),
                  s.NamedSessions.Count));

        FocusLengthChoices.Fill(MinutesCombo, minutes);
        _sessionsFull = count >= NamedSessions.Maximum;

        // Read against the mode this start would run in: a limiting mode needs a program kept usable,
        // "Open marked, allow all others" needs a program to open.
        _savedModes.Clear();
        _programFocusPossible = false;
        var sessions = new List<(string Key, string Name, string? CannotRun)>(stored.Count);
        foreach (var n in stored)
        {
            _savedModes[n.Key] = n.Mode;
            bool usable = FocusProgramLever.KeepsAProgramUsable(n.Programs, WindowsPrograms.Folder);
            bool marks = n.Programs.Any(p => p.StartWithSession);
            _programFocusPossible |= usable || marks;
            string? cannotRun = ProgramStartRules.LimitsPrograms(_chosenMode ?? n.Mode)
                ? (usable ? null : "PopOutNamedSessionCannotRunInfo")
                : (marks ? null : "PopOutNamedSessionNothingToOpenInfo");
            sessions.Add((n.Key, n.Name, cannotRun));
        }
        _runnableSessions.Clear();
        _runnableSessions.UnionWith(sessions.Where(n => n.CannotRun is null).Select(n => n.Key));
        ToolTipService.SetToolTip(ProgramFocusFrame,
            _programFocusPossible ? null : AppText.Get("PopOutStartProgramFocusInfo"));
        ProgramFocusChoice.IsEnabled = _programFocusPossible && !FocusSessionService.IsRunning;

        // A session or kind whose button is greyed cannot stay chosen, or Start would arm what the
        // window says cannot run.
        if ((_chosenSession is null || !_runnableSessions.Contains(_chosenSession))
            && sessions.FirstOrDefault(n => n.CannotRun is null).Key is { } first)
            _chosenSession = first;
        if (!_programFocusPossible && _chosenKind == FocusSessionKind.ProgramFocus)
            _chosenKind = FocusSessionKind.ScreenBreak;

        BuildSessionButtons(sessions);
        ShowChosenKind();
    }

    /// <summary>Puts the accent on the chosen kind, and shows the named sessions for Program focus or
    /// the goal for a screen break in the space beneath.</summary>
    private void ShowChosenKind()
    {
        bool programFocus = _chosenKind == FocusSessionKind.ProgramFocus;
        ProgramFocusChoice.Style = programFocus ? _accentStyle : null;
        ScreenBreakChoice.Style  = programFocus ? null : _accentStyle;
        // Neither is collapsed: the shared cell keeps the taller one's height in both modes.
        ShowInCell(NamedSessionChoices, programFocus);
        ShowInCell(GoalBox, !programFocus);
        foreach (var button in SessionButtons()) Reachable(button, programFocus);
        Reachable(GoalBox, !programFocus);

        // The modes belong to Program focus, so the menu part greys for a screen break.
        if (Descendant(StartButton, "SecondaryButton") is Control menuPart) menuPart.IsEnabled = programFocus;
        foreach (var item in ModeMenu.Items) item.IsEnabled = programFocus;
        MarkChosenMode();
    }

    /// <summary>Marks the mode this start would run in: the one picked, or the chosen session's saved one.</summary>
    private void MarkChosenMode()
    {
        var mode = _chosenMode
                   ?? (_chosenSession is not null && _savedModes.TryGetValue(_chosenSession, out var saved)
                           ? saved : ProgramFocusMode.AllowListed);
        foreach (var item in ModeMenu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = (ProgramFocusMode)item.Tag == mode;
    }

    /// <summary>Picks the mode for this start only; nothing is stored, and the session buttons are
    /// read against it again.</summary>
    private void OnChooseMode(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem { Tag: ProgramFocusMode mode }) return;
        _chosenMode = mode;
        RefusalText.Visibility = Visibility.Collapsed;
        ShowStartBox();
        ShowSession();
    }

    private static DependencyObject? Descendant(DependencyObject root, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element && element.Name == name) return child;
            if (Descendant(child, name) is { } deeper) return deeper;
        }
        return null;
    }

    private static void ShowInCell(UIElement element, bool shown)
    {
        element.Opacity          = shown ? 1 : 0;
        element.IsHitTestVisible = shown;
    }

    private static void Reachable(Control control, bool shown)
    {
        control.IsTabStop = shown;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(control,
            shown ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content
                  : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    private IEnumerable<ToggleButton> SessionButtons() =>
        NamedSessionChoices.Children.OfType<Grid>().SelectMany(f => f.Children.OfType<ToggleButton>());

    /// <summary>One button per named session, up to five, on the quick-button chrome. A session that
    /// cannot run is greyed with the reason on hover, on a frame since a disabled button shows none.</summary>
    private void BuildSessionButtons(IReadOnlyList<(string Key, string Name, string? CannotRun)> sessions)
    {
        var wanted = sessions.Select(n => $"{n.Key}|{n.CannotRun}|{n.Name}").ToList();
        if (!wanted.SequenceEqual(_sessionButtonsShown))
        {
            _sessionButtonsShown = wanted;
            _sessionGridShape    = default;
            NamedSessionChoices.Children.Clear();
            NamedSessionChoices.ColumnSpacing = PresetButtonLayout.Spacing;
            NamedSessionChoices.RowSpacing    = PresetButtonLayout.Spacing;
            ApplyCheckedChipResources(NamedSessionChoices);

            // The card's control shape, with no side inset: five buttons leave room for eight characters.
            var shape = RootGrid.Resources;
            var padding = (Thickness)shape["SessionControlPadding"];
            foreach (var (key, name, cannotRun) in sessions)
            {
                bool runs = cannotRun is null;
                var button = new ToggleButton
                {
                    Tag                 = key,
                    Style               = (Style)Application.Current.Resources["QuickButtonStyle"],
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment   = VerticalAlignment.Stretch,
                    MinHeight           = (double)shape["SessionControlHeight"],
                    FontSize            = (double)shape["SessionControlFontSize"],
                    Padding             = new Thickness(0, padding.Top, 0, padding.Bottom),
                    BorderThickness     = (Thickness)shape["SessionControlBorder"],
                    CornerRadius        = (CornerRadius)shape["SessionControlCornerRadius"],
                    IsEnabled           = runs && !FocusSessionService.IsRunning,
                    // Wrapped, never cut short: a long name takes another line.
                    Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
                button.Click += OnNamedSessionClick;

                var frame = new Grid { Background = new SolidColorBrush(Colors.Transparent) };
                frame.Children.Add(button);
                if (cannotRun is not null) ToolTipService.SetToolTip(frame, AppText.Get(cannotRun));
                NamedSessionChoices.Children.Add(frame);
            }
            LayoutSessionButtons();
        }
        MarkChosenSession();
    }

    /// <summary>Equal-width columns across the whole row; a short last row spreads its buttons over
    /// the spare columns, so every row ends flush.</summary>
    private void LayoutSessionButtons()
    {
        var frames = NamedSessionChoices.Children.OfType<Grid>().ToList();
        if (frames.Count == 0) return;

        double available = NamedSessionChoices.ActualWidth > 0 ? NamedSessionChoices.ActualWidth : SessionPanelWidth;
        var shape = PresetButtonLayout.Choose(frames.Count, available);
        if (shape == _sessionGridShape) return;
        _sessionGridShape = shape;

        NamedSessionChoices.ColumnDefinitions.Clear();
        NamedSessionChoices.RowDefinitions.Clear();
        for (int c = 0; c < shape.Columns; c++)
            NamedSessionChoices.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r < shape.Rows; r++)
            NamedSessionChoices.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        int index = 0;
        for (int row = 0; row < shape.Rows; row++)
        {
            int inRow    = Math.Min(shape.Columns, frames.Count - index);
            int baseSpan = shape.Columns / inRow;
            int extra    = shape.Columns % inRow;
            int column   = 0;
            for (int k = 0; k < inRow; k++, index++)
            {
                int span = baseSpan + (k < extra ? 1 : 0);
                Grid.SetRow(frames[index], row);
                Grid.SetColumn(frames[index], column);
                Grid.SetColumnSpan(frames[index], span);
                column += span;
            }
        }
    }

    /// <summary>Checks the chosen session's button, which then stops accepting clicks; weight as well
    /// as colour marks it.</summary>
    private void MarkChosenSession()
    {
        foreach (var button in SessionButtons())
        {
            bool chosen = (string?)button.Tag == _chosenSession;
            button.IsChecked        = chosen;
            button.IsHitTestVisible = !chosen;
            button.FontWeight = chosen ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }

    /// <summary>The chosen look: FocusDesk's accent with black text, legible on both themes. Set before
    /// the buttons are parented, so their templates resolve these.</summary>
    private static void ApplyCheckedChipResources(Panel panel)
    {
        var accent = new SolidColorBrush(AppColors.FromHex(Brand.ColorAmber));
        var onAccent = new SolidColorBrush(Colors.Black);
        foreach (string state in new[] { "", "PointerOver", "Pressed" })
        {
            panel.Resources[$"ToggleButtonBackgroundChecked{state}"] = accent;
            panel.Resources[$"ToggleButtonForegroundChecked{state}"] = onAccent;
        }
    }

    /// <summary>Chooses the named session for the next start and makes it the session in use, so the
    /// dashboard, Settings and the MQTT select agree. Nothing starts.</summary>
    private void OnNamedSessionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ButtonBase { Tag: string key }) return;

        // A toggle checks itself before Click is raised, so the pick is marked at once.
        _chosenSession = key;
        MarkChosenSession();
        MarkChosenMode();
        RefusalText.Visibility = Visibility.Collapsed;
        if (FocusSessionService.LeversAreLocked) return;
        SettingsService.Update(s => { if (NamedSessions.Find(s, key) is not null) s.SelectedNamedSession = key; });
    }

    /// <summary>The restrictions in force, one short line each. Tinted while any is and in the plain
    /// card fill while none is.</summary>
    private void ShowLevers()
    {
        var session = FocusSessionService.Current;
        string? screen = (session.DimsScreen, session.CoversScreen) switch
        {
            (true, true)  => "RestrictionScreenDimmedAndCovered",
            (true, false) => "RestrictionScreenDimmed",
            (false, true) => "RestrictionScreenCovered",
            _             => null,
        };

        var lines = new List<string>(4);
        if (session.BlocksNetwork) lines.Add(AppText.Get("RestrictionNetwork"));
        if (screen is not null) lines.Add(AppText.Get(screen));
        if (session.BlocksInput) lines.Add(AppText.Get("RestrictionInput"));
        if (session.LimitsPrograms) lines.Add(AppText.Get("RestrictionPrograms"));

        LeverText.Text = lines.Count > 0
            ? string.Join("\n", lines)
            : AppText.Get("RestrictionsNone");

        if (lines.Count > 0) LeverCard.Background = _activeTint;
        else LeverCard.ClearValue(Border.BackgroundProperty);
    }

    /// <summary>The tint an active card carries: the studio accent at a low alpha.</summary>
    private static SolidColorBrush ActiveTint()
    {
        var accent = AppColors.FromHex(Brand.ColorAmber);
        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(ActiveTintAlpha, accent.R, accent.G, accent.B));
    }

    /// <summary>The finished sessions, newest first. Rebuilt only where the set has changed, so a
    /// tick never moves a row out from under the pointer.</summary>
    private void ShowHistory()
    {
        var recent = FocusHistoryService.Recent(HistoryRowCount);
        var rows = recent.Select(FocusHistoryService.Describe).ToList();

        string shown = string.Join('\n', rows.Select(r => r.Summary + "\n" + r.Restrictions));
        if (shown == _historyShown) return;
        _historyShown = shown;

        HistoryRows.Children.Clear();
        if (rows.Count == 0)
        {
            HistoryRows.Children.Add(Line(AppText.Get("PopOutHistoryEmpty"), secondary: true));
            return;
        }

        foreach (var (summary, restrictions) in rows)
        {
            var row = new StackPanel();
            row.Children.Add(Line(summary, secondary: false));
            row.Children.Add(Line(restrictions, secondary: true));
            HistoryRows.Children.Add(row);
        }
    }

    /// <summary>One line of the list, wrapping at its spaces. The opacity rather than a theme brush:
    /// a brush from the application's own dictionary does not follow a light or dark switch.</summary>
    private static TextBlock Line(string text, bool secondary) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Opacity = secondary ? 0.65 : 1,
    };

    private void OnChooseProgramFocus(object sender, RoutedEventArgs e) => Choose(FocusSessionKind.ProgramFocus);

    private void OnChooseScreenBreak(object sender, RoutedEventArgs e) => Choose(FocusSessionKind.ScreenBreak);

    /// <summary>Chooses the kind the Start button hands on. Nothing starts, and nothing is stored: the
    /// stored default is what Home Assistant starts, and it stays as it was. A refusal from an earlier
    /// start was about the other choice, so it goes.</summary>
    private void Choose(FocusSessionKind chosen)
    {
        _chosenKind = chosen;
        RefusalText.Visibility = Visibility.Collapsed;
        ShowChosenKind();
    }

    /// <summary>Opens the focus-point window for the chosen kind and length, which starts the session
    /// after its time. Where the Focus page turns the focus point off, arms the session here at once.
    /// The focus-point rows are read now, so a window already open keeps what it opened with.</summary>
    private void OnStart(SplitButton sender, SplitButtonClickEventArgs e)
    {
        if (FocusLengthChoices.Selected(MinutesCombo) is not { } minutes) return;

        RefusalText.Visibility = Visibility.Collapsed;
        // Program focus starts with no goal; a screen break carries the one typed.
        bool programFocus = _chosenKind == FocusSessionKind.ProgramFocus;
        string goal = programFocus ? "" : FocusSessionGoal.Clean(GoalBox.Text);
        string? named = programFocus ? _chosenSession : null;
        ProgramFocusMode? mode = programFocus ? _chosenMode : null;
        var (shown, startsItself, seconds) = SettingsService.Read(
            s => (s.FocusPointShown, s.FocusPointStartsSession, s.FocusPointSeconds));
        if (shown)
        {
            FocusPointWindow.Open(_chosenKind, named, minutes, seconds, startsItself, goal, mode);
            return;
        }

        // The chosen length is what the next session runs for from wherever it is started, so it is
        // stored rather than held for this one start.
        SettingsService.Update(s => s.FocusSessionMinutes = minutes);
        var outcome = FocusSessionService.Arm(ActionCause.StatusWindow("Start button"), minutes, _chosenKind, goal, named, mode);
        if (outcome == FocusArmOutcome.Armed)
        {
            GoalBox.Text = "";
            Reload();
            return;
        }

        RefusalText.Text = Refusal(outcome);
        RefusalText.Visibility = Visibility.Visible;
    }

    /// <summary>Why nothing started, in the words of whoever pressed the button. Every one of these
    /// leaves the machine exactly as it was.</summary>
    private static string Refusal(FocusArmOutcome outcome) => AppText.Get(outcome switch
    {
        FocusArmOutcome.AlreadyRunning => "PopOutRefusalAlreadyRunning",
        FocusArmOutcome.LeverRefused   => "PopOutRefusalLeverRefused",
        FocusArmOutcome.NamedSessionMissing => "PopOutRefusalNamedSessionMissing",
        FocusArmOutcome.NothingToOpen  => "PopOutRefusalNothingToOpen",
        _                             => "PopOutRefusalLeverFailed",
    });

    /// <summary>The Program focus page's own save: a new session from the programs open now, shown in
    /// Settings with its name box focused.</summary>
    private async void OnSaveOpenPrograms(object sender, RoutedEventArgs e)
    {
        if (FocusSessionService.LeversAreLocked || _saving) return;
        _saving = true;
        ShowSession();
        try
        {
            var (key, leftOut, nothingOpen) = await ProgramFocusSettingsPanel.SaveOpenProgramsAsync();
            if (nothingOpen)
            {
                RefusalText.Text = AppText.Get("NamedSessionSaveOpenNothing");
                RefusalText.Visibility = Visibility.Visible;
            }
            if (key is null) return;
            ProgramFocusSettingsPanel.ShowSaved(key, leftOut);
            Dismiss();
        }
        catch (Exception ex) { AppLog.Error("StatusWindow.SaveOpenPrograms", ex); }
        finally
        {
            _saving = false;
            Reload();
        }
    }

    private bool _savingPositions;

    /// <summary>Saves where the running session's programs sit, for this monitor layout.</summary>
    private async void OnSavePositions(object sender, RoutedEventArgs e)
    {
        if (_savingPositions) return;
        _savingPositions = true;
        ShowSession();
        try
        {
            var (outcome, saved) = await ProgramPositions.SaveAsync(FocusSessionService.Current.NamedSession);
            SavePositionsResult.Text = ProgramFocusText.PositionsSaved(outcome, saved);
            SavePositionsResult.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { AppLog.Error("StatusWindow.SavePositions", ex); }
        finally
        {
            _savingPositions = false;
            ShowSession();
        }
    }

    /// <summary>Opens the Settings window, and stands down behind it.</summary>
    private void OnSettings(object sender, RoutedEventArgs e)
    {
        SettingsShellHost.Open(SettingsShellHost.FocusTag);
        Dismiss();
    }

    /// <summary>Escape dismisses as clicking away does — a hide, not a close, so the next click on
    /// the icon re-shows this same window.</summary>
    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Dismiss();
    }

    /// <summary>Sizes the window to what it holds and anchors it to the bottom-right of the work area
    /// of the monitor the pointer is on, which is the monitor whose notification area was clicked.</summary>
    private void Place()
    {
        RootGrid.Measure(new Size(PopOutPlacement.WidthInUnits, double.PositiveInfinity));

        // Any frame the window still carries is added on top, so the content keeps the width it was
        // measured at and all of its height.
        var frame = new SizeInt32(
            AppWindow.Size.Width  - AppWindow.ClientSize.Width,
            AppWindow.Size.Height - AppWindow.ClientSize.Height);

        var (work, scale) = MonitorMetrics.ForCursor();
        var rect = PopOutPlacement.BottomRight(work, scale, RootGrid.DesiredSize.Height, frame);

        AppWindow.Resize(new SizeInt32(rect.Width, rect.Height));
        AppWindow.Move(new PointInt32(rect.X, rect.Y));
    }
}
