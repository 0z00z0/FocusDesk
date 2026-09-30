using System.Diagnostics;
using System.Globalization;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Point = Windows.Foundation.Point;

namespace FocusDesk.UI;

/// <summary>
/// The one-minute breathing exercise shown when the pop-out's Start button is pressed, before any
/// session exists. The ring fills over the minute, then a button starts the session that was chosen.
/// Full-screen and frameless on the monitor under the pointer, the same weight as the screen cover
/// it precedes.
/// </summary>
/// <remarks>
/// <para>Closing it — Escape, Alt+F4, or a click on the screen away from the ring and the button —
/// at any point cancels: nothing is armed and nothing on the machine changes. The button is the
/// only way on to a session.</para>
/// <para>The same for both kinds of session. What follows the button is the session's own business:
/// a screen break's cover opens straight on its configured visual, a program focus minimises the
/// other programs.</para>
/// </remarks>
internal sealed partial class FocusPointWindow : Window
{
    // The exercise's circle, in its 220-unit canvas.
    private const double Cx     = 110;
    private const double Cy     = 110;
    private const double Radius = 102;

    /// <summary>Half a breath: five seconds out and five back, so the dot pulses every ten.</summary>
    private static readonly TimeSpan BreathHalf = TimeSpan.FromSeconds(5);

    /// <summary>How long the line under the point stays, and how long its fade takes.</summary>
    private static readonly TimeSpan HintStays = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan HintFade  = TimeSpan.FromMilliseconds(800);

    private static readonly TimeSpan ButtonFade = TimeSpan.FromMilliseconds(1200);

    private static FocusPointWindow? _open;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly FocusPointStart _start;
    private readonly DispatcherTimer _minute = new();
    private readonly bool _animate = MotionPreference.AnimationsAllowed();

    private Storyboard? _breath;
    private bool _drawsEveryFrame;
    private bool _placed;

    /// <summary>Opens the exercise for a session of <paramref name="kind"/> lasting
    /// <paramref name="minutes"/>. One already open is closed first, which cancels it.</summary>
    internal static void Open(FocusSessionKind kind, int minutes)
    {
        _open?.Close();

        var window = new FocusPointWindow(kind, minutes);
        _open = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_open, window)) _open = null; };
        window.Activate();
    }

    private FocusPointWindow(FocusSessionKind kind, int minutes)
    {
        InitializeComponent();
        _start = new FocusPointStart(kind, minutes, () => _clock.Elapsed);

        Title = AppText.Get("FocusPointWindowTitle");
        StartSessionButton.Content = AppText.Get("FocusPointWindowStart");
        // The exercise sets its line in capitals; the text itself is the interface language's.
        Hint.Text = AppText.Get("FocusPointWindowHint").ToUpper(CultureInfo.CurrentCulture);

        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);

        // The presenter keeps a dialog frame with the border turned off: three pixels on every side.
        NativeMethods.RemoveFrame(Win32Interop.GetWindowFromWindowId(AppWindow.Id));

        _minute.Interval = _start.Remaining;
        _minute.Tick += (_, _) => OnMinuteUp();

        StartBreathing();
        FadeHint();
        _minute.Start();

        Activated += OnActivated;
        Closed    += OnClosed;
    }

    /// <summary>Sets the dot breathing and the arc drawing every frame. Where the machine asks for no
    /// animation the dot holds still at its resting glow and the arc moves once a second on the
    /// timer instead.</summary>
    private void StartBreathing()
    {
        DrawArc();

        if (!_animate)
        {
            GlowScale.ScaleX = GlowScale.ScaleY = 1.15;
            Glow.Opacity     = 0.83;
            _minute.Interval = TimeSpan.FromSeconds(1);
            return;
        }

        // The glow swells further than the dot, as its blur widens in the exercise.
        _breath = new Storyboard { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        _breath.Children.Add(Breath(DotScale,  "ScaleX",  1,   1.18));
        _breath.Children.Add(Breath(DotScale,  "ScaleY",  1,   1.18));
        _breath.Children.Add(Breath(GlowScale, "ScaleX",  1,   1.4));
        _breath.Children.Add(Breath(GlowScale, "ScaleY",  1,   1.4));
        _breath.Children.Add(Breath(Glow,      "Opacity", 0.6, 1));
        _breath.Begin();

        CompositionTarget.Rendering += OnFrame;
        _drawsEveryFrame = true;
    }

    /// <summary>One half of the breath on the curve a browser's ease-in-out follows, so the dot swells
    /// exactly as the exercise's page does.</summary>
    private static DoubleAnimationUsingKeyFrames Breath(DependencyObject target, string property,
                                                        double from, double to)
    {
        var animation = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = true };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = from });
        animation.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime   = KeyTime.FromTimeSpan(BreathHalf),
            Value     = to,
            KeySpline = new KeySpline { ControlPoint1 = new Point(0.42, 0), ControlPoint2 = new Point(0.58, 1) },
        });
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private void OnFrame(object? sender, object e) => DrawArc();

    /// <summary>The arc fills clockwise from twelve o'clock over the minute, whole at its end.</summary>
    private void DrawArc()
    {
        double done = Math.Clamp(1 - _start.Remaining / FocusPointStart.Length, 0, 1);
        Progress.Data = RingGeometry.Arc(Cx, Cy, Radius, 0, 360 * done);
    }

    private void StopDrawing()
    {
        if (_drawsEveryFrame) CompositionTarget.Rendering -= OnFrame;
        _drawsEveryFrame = false;
    }

    private void FadeHint() => Fade(Hint, 1, 0, HintStays, HintFade);

    /// <summary>Checks the minute against the start's own clock rather than trusting the timer, and
    /// reveals the button only once the start says it may be pressed.</summary>
    private void OnMinuteUp()
    {
        DrawArc();
        if (!_start.CanBegin)
        {
            if (_start.Remaining > TimeSpan.Zero && _minute.Interval > _start.Remaining)
                _minute.Interval = _start.Remaining;
            return;
        }

        _minute.Stop();
        StopDrawing();
        StartSessionButton.Visibility = Visibility.Visible;
        Fade(StartSessionButton, 0, 1, TimeSpan.Zero, ButtonFade);
    }

    /// <summary>An opacity fade, or the end value at once where the machine asks for no animation.</summary>
    private void Fade(UIElement target, double from, double to, TimeSpan after, TimeSpan length)
    {
        target.Opacity = from;
        var fade = new DoubleAnimation
        {
            From                     = from,
            To                       = to,
            BeginTime                = after,
            Duration                 = new Duration(_animate ? length : TimeSpan.Zero),
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(fade, target);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var board = new Storyboard();
        board.Children.Add(fade);
        board.Begin();
    }

    /// <summary>Starts the session that was chosen. The window leaves the screen first, so a screen
    /// break's cover goes up over the desktop rather than over this window.</summary>
    private void OnStartSession(object sender, RoutedEventArgs e)
    {
        if (!_start.CanBegin) return;

        AppWindow.Hide();
        var outcome = _start.Begin(Arm);
        Close();

        // The window that would have shown why is gone, so the pop-out says it instead.
        if (outcome is { } refused && refused != FocusArmOutcome.Armed) StatusWindow.OpenWithRefusal(refused);
    }

    /// <summary>The arm the pop-out's Start button made before this window stood in front of it. The
    /// length is stored for the next session from wherever it is started; the kind is not, so the
    /// stored default — what Home Assistant starts — stays as it was.</summary>
    private static FocusArmOutcome Arm(int minutes, FocusSessionKind kind)
    {
        SettingsService.Update(s => s.FocusSessionMinutes = minutes);
        return FocusSessionService.Arm(ActionCause.FocusPointWindow(), minutes, kind);
    }

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    /// <summary>The way out with no close button on view: a tap on the screen away from the ring and
    /// the button cancels, the same as Escape. The ring, the hint and the button are not ancestors of
    /// the background in the visual tree, so a tap on any of them never reaches here.</summary>
    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs e) => Close();

    /// <summary>However the window closes, the start is settled without arming.</summary>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _start.Cancel();
        _minute.Stop();
        _breath?.Stop();
        StopDrawing();
    }

    /// <summary>Full-screen on the monitor under the pointer the first time it is shown, which is the
    /// monitor the pop-out's Start button was pressed on — its whole panel, the taskbar strip
    /// included, the same as the screen cover this window precedes.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_placed || e.WindowActivationState == WindowActivationState.Deactivated) return;
        _placed = true;

        try
        {
            var bounds = NativeMethods.DisplayBoundsForCursor();
            if (bounds is null)
            {
                var all = NativeMethods.AllDisplayBounds();
                if (all.Count > 0) bounds = all[0];
            }
            if (bounds is { } b) AppWindow.MoveAndResize(b);
        }
        catch (Exception ex) { AppLog.Error("FocusPointWindow.Place", ex); }
    }
}
