using System.Globalization;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Graphics;
using ZeroZero.Brand.Core;
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
/// <para>Nothing here ends a session. The start box starts one; the way out is Home Assistant's
/// staged cancel and the session's own clock, and the window says so while one runs.</para>
/// <para>The first time the pop-out is on screen after a session arms, it shows the session-start
/// focus point in place of the countdown, once per session. The header's button brings the page back
/// at any time, and the button under the page returns to the countdown. Hiding the window drops the
/// page, so no browser runs behind a hidden pop-out.</para>
/// </remarks>
internal sealed partial class StatusWindow : Window
{
    /// <summary>How many finished sessions the list shows. Five rows of up to two lines each keep the
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

    /// <summary>The pop-out browser's own store, apart from the screen cover's. Two environments
    /// over one store work only while every option matches, and a mismatch refuses whichever starts
    /// second; a store of its own cannot fail that way.</summary>
    private const string PageBrowserFolder = "WebView2-PopOut";

    /// <summary>The host name the bundled page is served under, in the reserved <c>.invalid</c>
    /// namespace so it can never resolve to anything on the network.</summary>
    private const string PageHost = "pop-out.focusdesk.invalid";

    private const string PageFile = "session-start.html";

    /// <summary>The page's own background, so the browser shows no light frame while it loads.</summary>
    private static readonly Windows.UI.Color PageGround = Windows.UI.Color.FromArgb(0xFF, 0x0B, 0x0E, 0x11);

    /// <summary>One browser environment for the process, kept across pop-out windows.</summary>
    private static Task<CoreWebView2Environment>? _browser;

    private static StatusWindow? _open;

    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _idleClose = new() { Interval = IdleCloseAfter };
    private readonly SolidColorBrush _ringFill = new();
    private readonly SolidColorBrush _activeTint = ActiveTint();

    /// <summary>What the history list was last built from, so a tick that changed nothing does not
    /// rebuild rows under the pointer.</summary>
    private string? _historyShown;

    /// <summary>The focus point's browser while the page is showing, and null otherwise.</summary>
    private WebView2? _page;

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

    private StatusWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.Name} status";

        ConfigureChrome();

        RingTrack.Data = RingGeometry.Arc(
            RingCentre, RingCentre, RingRadius, RingGeometry.StartAngle, RingGeometry.Sweep);
        RingFill.Stroke = _ringFill;

        string showFocusPoint = AppText.Get("PopOutFocusPointShow");
        FocusPointButtonText.Text = showFocusPoint;
        AutomationProperties.SetName(FocusPointButton, showFocusPoint);
        SkipButton.Content = AppText.Get("PopOutFocusPointSkip");

        // The session moves on its own clock and from Home Assistant, so the window follows both:
        // the engine's event for a stage moving, and a tick for the minutes running down.
        FocusSessionService.Changed += OnSessionChanged;
        _tick.Tick += (_, _) => ShowSession();
        _idleClose.Tick += (_, _) => CloseIfIdle();

        // A line that appears while the window is open — the way out once a session starts, a
        // refusal — changes the content's height, and the window follows it rather than clipping.
        ContentPanel.SizeChanged += (_, _) => { if (AppWindow.IsVisible) Place(); };

        Activated += OnActivated;

        Closed += (_, _) =>
        {
            _tick.Stop();
            _idleClose.Stop();
            FocusSessionService.Changed -= OnSessionChanged;
            DropPage();
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
        AppWindow.IsShownInSwitchers = false;
    }

    /// <summary>Reads everything the window shows, places it, and brings it forward.</summary>
    private void ShowPopOut()
    {
        _idleClose.Stop();
        Reload();
        if (FocusSessionService.TakeOpeningPage()) ShowFocusPoint();
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
        ShowCountdown();
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

    private void OnSessionChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        Reload();
        OfferFocusPoint();
    });

    /// <summary>Shows the focus point where a session has armed since it was last offered. Only a
    /// window on screen takes the offer: one that is hidden leaves it for its next show.</summary>
    private void OfferFocusPoint()
    {
        if (AppWindow.IsVisible && FocusSessionService.TakeOpeningPage()) ShowFocusPoint();
    }

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
        if (offered) FocusLengthChoices.Fill(MinutesCombo, minutes);
    }

    /// <summary>What the session holds. Tinted while a lever is in force and in the plain card fill
    /// while none is, so the card carries the state without a word for it.</summary>
    private void ShowLevers()
    {
        var session = FocusSessionService.Current;
        var held = new List<string>(2);
        if (session.DimsScreen)   held.Add("dimmed");
        if (session.CoversScreen) held.Add("covered");

        var lines = new List<string>(3);
        if (session.BlocksNetwork) lines.Add("The network is blocked until the session ends, except for the broker and the allowed programs.");
        if (held.Count > 0) lines.Add($"The screen is {string.Join(" and ", held)} until the session ends.");
        if (session.BlocksInput) lines.Add("The mouse and keyboard are blocked until the session ends.");
        if (session.LimitsPrograms) lines.Add(AppText.Get("PopOutProgramsLimited"));

        LeverText.Text = lines.Count > 0
            ? string.Join("\n", lines)
            : "Nothing is held. The screen is as it was.";

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
        string[] lines = recent.Count == 0
            ? ["No session has finished yet."]
            : [.. recent.Select(FocusHistoryService.Describe)];

        string shown = string.Join('\n', lines);
        if (shown == _historyShown) return;
        _historyShown = shown;

        HistoryRows.Children.Clear();
        foreach (string line in lines)
            HistoryRows.Children.Add(Line(line, secondary: recent.Count == 0));
    }

    /// <summary>One line of the list. The opacity rather than a theme brush: a brush looked up from
    /// the application's own dictionary does not follow a light or dark switch, and these lines are
    /// built in code rather than in markup where the theme reference would. Two lines at most, so the
    /// row cap bounds the window's height.</summary>
    private static TextBlock Line(string text, bool secondary) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        MaxLines = 2,
        TextTrimming = TextTrimming.CharacterEllipsis,
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
            OfferFocusPoint();
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
        FocusArmOutcome.LeverRefused   => "No chosen lever would take it: no brightness change, nothing to cover, or no rights or broker for a block.",
        _                              => "Something the session needed failed to engage. Whatever did engage has been lifted again.",
    };

    private void OnShowFocusPoint(object sender, RoutedEventArgs e) => ShowFocusPoint();

    private void OnSkipFocusPoint(object sender, RoutedEventArgs e) => ShowCountdown();

    /// <summary>Puts the focus point in place of the countdown. A page already showing is left to
    /// run rather than started over.</summary>
    private void ShowFocusPoint()
    {
        SessionView.Visibility      = Visibility.Collapsed;
        FocusPointButton.Visibility = Visibility.Collapsed;
        FocusPointView.Visibility   = Visibility.Visible;
        if (_page is null) StartPage();
    }

    /// <summary>Puts the countdown back and closes the page's browser.</summary>
    private void ShowCountdown()
    {
        DropPage();
        FocusPointView.Visibility   = Visibility.Collapsed;
        SessionView.Visibility      = Visibility.Visible;
        FocusPointButton.Visibility = Visibility.Visible;
    }

    /// <summary>Closes the browser's own processes, which otherwise outlive the control.</summary>
    private void DropPage()
    {
        var page = _page;
        _page = null;
        if (page is null) return;

        try { page.Close(); }
        catch (Exception ex) { AppLog.Error("StatusWindow.ClosePage", ex); }
        try { FocusPointHost.Children.Clear(); }
        catch (Exception ex) { AppLog.Error("StatusWindow.ClearPage", ex); }
    }

    /// <summary>Builds the embedded browser and points it at the bundled page. Fire-and-forget, so
    /// nothing escapes it: a failure logs and the countdown comes back. The page keeps its pointer
    /// and keyboard input, because its own clicks and keys start and restart its countdown.</summary>
    private async void StartPage()
    {
        var view = new WebView2 { DefaultBackgroundColor = PageGround };
        FocusPointHost.Children.Add(view);
        _page = view;

        try
        {
            // A store that failed to open is tried afresh rather than failing every later attempt.
            if (_browser is { IsFaulted: true } or { IsCanceled: true }) _browser = null;

            // An explicit store. The default for an unpackaged application sits beside the
            // executable, in a program folder the session's user cannot write to.
            _browser ??= CoreWebView2Environment.CreateWithOptionsAsync(
                "", AppPaths.DataFile(PageBrowserFolder), new CoreWebView2EnvironmentOptions())
                .AsTask();

            await view.EnsureCoreWebView2Async(await _browser);

            // Skipped or hidden while the browser was starting: the page is no longer wanted.
            if (!ReferenceEquals(_page, view)) return;

            var core = view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                PageHost,
                Path.Combine(AppContext.BaseDirectory, "Assets", "FocusPoint"),
                CoreWebView2HostResourceAccessKind.Allow);

            // A page, not a browser: no menu, no developer tools, no browser shortcuts, no zoom.
            core.Settings.AreDevToolsEnabled               = false;
            core.Settings.AreDefaultContextMenusEnabled    = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled               = false;
            core.Settings.IsZoomControlEnabled             = false;

            core.NavigationCompleted += (_, args) =>
            {
                // Queued rather than run here, so the browser is not closed from inside its own event.
                if (args.IsSuccess) return;
                var status = args.WebErrorStatus;
                DispatcherQueue.TryEnqueue(() => FallBackToCountdown(view, $"navigation failed: {status}"));
            };
            view.Source = new Uri($"https://{PageHost}/{PageFile}");
        }
        catch (Exception ex)
        {
            AppLog.Error("StatusWindow.StartPage", ex);
            FallBackToCountdown(view, "the embedded browser did not start");
        }
    }

    /// <summary>Returns to the countdown where the page could not be shown, unless the page was
    /// already left.</summary>
    private void FallBackToCountdown(WebView2 view, string why)
    {
        if (!ReferenceEquals(_page, view)) return;
        AppLog.Info($"Pop-out: the focus point could not be shown ({why}), so the countdown shows instead.");
        ShowCountdown();
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
