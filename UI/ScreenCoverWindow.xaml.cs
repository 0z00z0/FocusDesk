using System.Globalization;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;

namespace FocusDesk.UI;

/// <summary>
/// One display's black cover, held up for the length of a focus session. It takes the screen and
/// nothing else: it never activates, the mouse passes through it, and whatever had the keyboard
/// keeps it, so a program already running carries on.
/// </summary>
/// <remarks>
/// <para>Bounds come from the display this cover is for, in physical pixels, so a mixed-scale
/// arrangement leaves no strip uncovered. The panel it reveals is sized in device-independent units
/// and scaled as one, so it is the same physical size whatever that display's scale factor is.</para>
/// <para>Two visuals, chosen per session. The dial is drawn here; the focus point is a bundled page
/// drawn by an embedded browser and fed the same countdown through
/// <see cref="CoverSessionMessage"/>. A browser that cannot start falls back to the dial, because a
/// cover that draws nothing is a black screen nobody can explain.</para>
/// <para>Nothing here ends a session. The cover goes when the session does, and a run that died
/// leaves no cover behind because a window does not outlive its process.</para>
/// </remarks>
internal sealed partial class ScreenCoverWindow : Window
{
    // The dial's geometry, in the same 220-unit canvas the panel is laid out in.
    private const double Cx = 110;
    private const double Cy = 110;
    private const double TickOuterRadius = 100;
    private const double MinorTickLength = 9;
    private const double MajorTickLength = 17;
    private const double HairlineRadius  = 78;
    private const double LeadDotSize     = 11;

    /// <summary>How much of the circle the radar sweep's comet covers.</summary>
    private const double SweepArcDegrees = 60;

    /// <summary>One turn of the sweep. Slow enough to read as weather rather than as motion, and the
    /// only thing on the cover that moves during a long session where the countdown barely stirs.</summary>
    private static readonly TimeSpan SweepTurn      = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan SweepTurnFinal = TimeSpan.FromSeconds(40);

    /// <summary>Half a breath. Doubled by the reverse, so the ordinary rhythm is the ten seconds the
    /// focus-point page breathes on and the two visuals read as one family.</summary>
    private static readonly TimeSpan BreathHalf      = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BreathHalfFinal = TimeSpan.FromSeconds(2);

    /// <summary>The browser's own store, beside the settings rather than beside the executable: the
    /// program folder is not writable for the user the session runs as.</summary>
    private const string BrowserDataFolder = "WebView2";

    /// <summary>The host name the bundled page is served under. A name in the reserved
    /// <c>.invalid</c> namespace, so it can never resolve to anything on the network.</summary>
    private const string PageHost = "focus-point.focusdesk.invalid";

    private const string PageFile = "focus-point.html";

    /// <summary>One browser environment for the process, however many displays are covered. Two
    /// environments over one store are only allowed where every option matches, which is a
    /// constraint worth not having.</summary>
    private static Task<CoreWebView2Environment>? _browser;

    private readonly SolidColorBrush _fill = new();

    private readonly bool _animate;

    private readonly IntPtr _hwnd;
    private readonly NativeMethods.ClosingRefusal? _refusal;

    private CoverVisual _visual;

    private Storyboard? _sweep;
    private Storyboard? _breath;
    private bool _pacedForFinalStretch;

    private WebView2? _page;
    private bool _pageIsReady;
    private string? _unsent;

    internal ScreenCoverWindow(CoverVisual visual)
    {
        InitializeComponent();
        Title = "FocusDesk focus session";

        _visual  = visual;
        _animate = MotionPreference.AnimationsAllowed();

        TickLit.Stroke  = _fill;
        RingFill.Stroke = _fill;
        LeadDot.Fill    = _fill;
        UnitText.Text   = AppText.Get("CoverMinutesLeft");

        // A whole spent dial until the first reading lands, so no frame shows an empty canvas.
        TickSpent.Data = GaugeTicks.Range(Cx, Cy, TickOuterRadius, 0, GaugeTicks.Count,
                                          MinorTickLength, MajorTickLength);
        SweepArc.Data  = RingGeometry.Arc(Cx, Cy, TickOuterRadius, 360 - SweepArcDegrees,
                                          SweepArcDegrees);

        if (_animate) Pace(finalStretch: false);
        else          SweepArc.Visibility = Visibility.Collapsed;

        AppWindow.IsShownInSwitchers = false;

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);

        // Before the first show: an extended style set afterwards is not reliably picked up.
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        NativeMethods.MakeClickThroughAndUnfocusable(_hwnd);

        // Alt+F4, the switcher's close and an ordinary End task all reach a window as a close
        // request, which is dropped. Ending the process is what this cannot stop.
        _refusal = NativeMethods.RefuseClose(_hwnd);

        if (_visual == CoverVisual.FocusPoint) StartPage();
    }

    /// <summary>Whether this cover has gone. The handle answers no once the window is destroyed,
    /// which is how the tick notices a cover that was taken down under it.</summary>
    internal bool IsGone => !NativeMethods.WindowExists(_hwnd);

    /// <summary>Takes this cover down. The one close that is meant to work: the refusal is lifted
    /// first, so the session's own teardown and a rebuild are not fighting the guard.</summary>
    internal void Dismiss()
    {
        _sweep?.Stop();
        _breath?.Stop();

        // Closes the browser's own processes; they otherwise outlive the window.
        try { _page?.Close(); }
        catch (Exception ex) { AppLog.Error("ScreenCoverWindow.ClosePage", ex); }
        _page = null;

        _refusal?.Allow();
        Close();
    }

    /// <summary>Puts the cover over <paramref name="bounds"/> — one display's whole panel in
    /// physical pixels, the taskbar's strip included — and shows it without taking focus.</summary>
    internal void Cover(RectInt32 bounds)
    {
        AppWindow.MoveAndResize(bounds);
        AppWindow.Show(activateWindow: false);
        KeepOnTop();
    }

    /// <summary>Puts the cover back at the top of the topmost band and asserts its styles again.
    /// Called on the tick: a window created topmost after this one sits above it until this runs
    /// again, and a style the framework puts back is corrected within the same second rather than
    /// leaving the cover able to take focus.</summary>
    internal void KeepOnTop()
    {
        NativeMethods.RaiseToTopmost(_hwnd);
        NativeMethods.MakeClickThroughAndUnfocusable(_hwnd);
    }

    /// <summary>Draws the countdown and decides whether the panel is showing.</summary>
    /// <remarks>The focus point is the whole cover and stands for the session's length, so the reveal
    /// that governs the dial does not apply to it.</remarks>
    internal void Apply(FocusCoverReading? reading, string levers, bool revealed,
                        CoverAppearance appearance)
    {
        if (_visual == CoverVisual.FocusPoint)
        {
            if (reading is { } session) Send(session, appearance);
            Reveal.Visibility = Visibility.Collapsed;
            return;
        }

        if (reading is { } r) Draw(r);

        LeversText.Text   = levers;
        Reveal.Opacity    = appearance.Intensity;
        Reveal.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The dial at one reading: which marks are still lit, the hairline's exact remainder,
    /// the leading mark's place on the arc, and what the middle reads.</summary>
    private void Draw(FocusCoverReading r)
    {
        int lit = GaugeTicks.LitCount(r.DialFraction);

        TickLit.Data = GaugeTicks.Range(Cx, Cy, TickOuterRadius, 0, lit,
                                        MinorTickLength, MajorTickLength);
        TickSpent.Data = GaugeTicks.Range(Cx, Cy, TickOuterRadius, lit, GaugeTicks.Count,
                                          MinorTickLength, MajorTickLength);
        RingFill.Data = RingGeometry.Arc(Cx, Cy, HairlineRadius, RingGeometry.StartAngle,
                                         RingGeometry.Sweep * r.FractionLeft);

        _fill.Color = AppColors.FromPacked(r.Argb);

        var at = RingGeometry.At(Cx, Cy, TickOuterRadius - MinorTickLength / 2,
                                 GaugeTicks.AngleOf(Math.Max(0, lit - 1)));
        Canvas.SetLeft(LeadDot, at.X - LeadDotSize / 2);
        Canvas.SetTop(LeadDot, at.Y - LeadDotSize / 2);
        LeadDot.Opacity = lit > 0 ? 1 : 0;

        // Inside the final minute the instrument rereads itself: the middle counts seconds and each
        // mark is one of them, so the last minute is watched at the resolution it deserves.
        AmountText.Text = r.IsFinalStretch
            ? ((int)Math.Ceiling(r.SecondsLeft)).ToString(CultureInfo.CurrentCulture)
            : r.MinutesLeft.ToString(CultureInfo.CurrentCulture);
        UnitText.Text = AppText.Get(r.IsFinalStretch ? "CoverSecondsLeft" : "CoverMinutesLeft");

        if (_animate && r.IsFinalStretch != _pacedForFinalStretch) Pace(r.IsFinalStretch);
    }

    /// <summary>Sets the two rhythms. The final stretch quickens the breath and slows the sweep, so
    /// the cover reads as nearly done without ever reading as an alarm.</summary>
    private void Pace(bool finalStretch)
    {
        _pacedForFinalStretch = finalStretch;

        _sweep?.Stop();
        _breath?.Stop();

        SweepArc.Opacity = finalStretch ? 0.35 : 1;

        _sweep = Spin(SweepRotate, finalStretch ? SweepTurnFinal : SweepTurn);
        _sweep.Begin();

        var half = finalStretch ? BreathHalfFinal : BreathHalf;
        _breath = new Storyboard { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        _breath.Children.Add(Pulse(LeadScale, "ScaleX",  1, 1.4,  half));
        _breath.Children.Add(Pulse(LeadScale, "ScaleY",  1, 1.4,  half));
        _breath.Children.Add(Pulse(Readout,   "Opacity", 1, 0.78, half));
        _breath.Begin();
    }

    private static Storyboard Spin(DependencyObject target, TimeSpan turn)
    {
        var turning = new DoubleAnimation
        {
            From                     = 0,
            To                       = 360,
            Duration                 = new Duration(turn),
            RepeatBehavior           = RepeatBehavior.Forever,
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(turning, target);
        Storyboard.SetTargetProperty(turning, "Angle");

        var board = new Storyboard();
        board.Children.Add(turning);
        return board;
    }

    private static DoubleAnimation Pulse(DependencyObject target, string property,
                                         double from, double to, TimeSpan half)
    {
        var animation = new DoubleAnimation
        {
            From                     = from,
            To                       = to,
            Duration                 = new Duration(half),
            EasingFunction           = new SineEase { EasingMode = EasingMode.EaseInOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    /// <summary>Builds the embedded browser and points it at the bundled page. Fire-and-forget by
    /// nature — a window constructor cannot wait for a browser — so nothing is allowed to escape it:
    /// a failure logs and the dial is drawn instead.</summary>
    private async void StartPage()
    {
        try
        {
            var view = new WebView2
            {
                DefaultBackgroundColor = Colors.Black,
                IsTabStop              = false,
            };
            FocusPointHost.Children.Add(view);
            _page = view;

            // An explicit store. The default for an unpackaged application sits beside the
            // executable, in a program folder the session's user cannot write to.
            _browser ??= CoreWebView2Environment.CreateWithOptionsAsync(
                "", AppPaths.DataFile(BrowserDataFolder), new CoreWebView2EnvironmentOptions())
                .AsTask();

            await view.EnsureCoreWebView2Async(await _browser);

            var core = view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                PageHost,
                Path.Combine(AppContext.BaseDirectory, "Assets", "FocusPoint"),
                CoreWebView2HostResourceAccessKind.Allow);

            // A cover is not a browser: nothing here navigates, opens a menu or takes a shortcut.
            core.Settings.AreDevToolsEnabled               = false;
            core.Settings.AreDefaultContextMenusEnabled    = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsStatusBarEnabled               = false;
            core.Settings.IsZoomControlEnabled             = false;

            core.NavigationCompleted += (_, args) =>
            {
                // A page that did not load is the browser's own error page, light-coloured and over
                // the whole display: the dial replaces it.
                // Queued rather than run here, so the browser is not closed from inside its own event.
                if (!args.IsSuccess)
                {
                    var status = args.WebErrorStatus;
                    DispatcherQueue.TryEnqueue(() => FallBackToDial($"navigation failed: {status}"));
                    return;
                }
                _pageIsReady = true;
                Flush();
            };
            view.Source = new Uri($"https://{PageHost}/{PageFile}");

            // The browser takes activation as it comes up; the cover must not keep it.
            KeepOnTop();
        }
        catch (Exception ex)
        {
            AppLog.Error("ScreenCoverWindow.StartPage", ex);
            FallBackToDial("the embedded browser did not start");
        }
    }

    /// <summary>Drops the page and draws the dial from the next tick on.</summary>
    private void FallBackToDial(string why)
    {
        AppLog.Info($"Focus: the focus point could not be shown ({why}), so the cover draws its dial instead.");

        _visual      = CoverVisual.Ring;
        _pageIsReady = false;
        try { _page?.Close(); } catch { /* the browser may already be gone */ }
        try { FocusPointHost.Children.Clear(); } catch { /* the window may already be gone */ }
        _page = null;
    }

    /// <summary>Hands the page the session. The latest message is kept, so a reading that arrives
    /// before the page has loaded is sent the moment it has rather than dropped.</summary>
    private void Send(FocusCoverReading reading, CoverAppearance appearance)
    {
        _unsent = CoverSessionMessage.Compose(reading, appearance,
                                              AppText.Get("CoverFocusPointHint"),
                                              AppText.Get("CoverFocusPointDone"));
        Flush();
    }

    private void Flush()
    {
        if (!_pageIsReady || _unsent is not { } message || _page?.CoreWebView2 is not { } core) return;

        try { core.PostWebMessageAsJson(message); }
        catch (Exception ex) { AppLog.Error("ScreenCoverWindow.Flush", ex); }
        _unsent = null;
    }
}
