using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using ZeroZero.SettingsShell.WinUI;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>
/// The Settings window: the shared shell, holding this application's seven pages. One window at a
/// time — a second request brings the open one forward rather than opening another.
/// </summary>
/// <remarks>The shell owns the window, the navigation pane, the placement and Escape; everything
/// inside a page is this application's.</remarks>
internal static class SettingsShellHost
{
    public const string FocusTag  = "focus";
    public const string ProgramFocusTag = "program-focus";
    public const string ScreenBreakTag = "screen-break";
    public const string MqttTag       = "mqtt";
    public const string AppearanceTag = "appearance";
    public const string AboutTag       = "about";
    public const string DiagnosticsTag = "diagnostics";

    private static SettingsWindow? _window;

    /// <summary>Opens the Settings window at a section, or brings the open one to that section.</summary>
    public static void Open(string? tag = null)
    {
        if (_window is { } open)
        {
            if (tag is not null) open.Navigate(tag);
            open.Activate();
            return;
        }

        FocusSettingsPanel? focus = null;
        ProgramFocusSettingsPanel? programFocus = null;
        ScreenSettingsPanel? screen = null;
        MqttSettingsPage? mqtt = null;
        AppearanceSettingsPanel? appearance = null;
        AboutSettingsPanel? about = null;
        DiagnosticsSettingsPanel? diagnostics = null;

        var rectStore = new SettingsWindowRectStore();

        var window = new SettingsWindow(new SettingsWindowSetup
        {
            Title = $"{AppInfo.Name} settings",
            Sections =
            [
                new SettingsSection
                {
                    Tag = FocusTag, Label = AppText.Get("SettingsFocusSessionPage"),
                    Icon = NavIcon("focus"),
                    Build = () => focus = new FocusSettingsPanel(),
                    // The session moves on its own clock, so the page follows it only while it is
                    // the one on screen.
                    Enter = () => focus?.Watch(),
                    Leave = () => focus?.Unwatch(),
                },
                new SettingsSection
                {
                    Tag = ProgramFocusTag, Label = AppText.Get("FocusProgramFocusHeading"),
                    Icon = NavIcon("program-focus"),
                    Build = () => programFocus = new ProgramFocusSettingsPanel(),
                    Enter = () => programFocus?.Watch(),
                    Leave = () => programFocus?.Unwatch(),
                },
                new SettingsSection
                {
                    Tag = ScreenBreakTag, Label = AppText.Get("FocusScreenBreakHeading"),
                    Icon = NavIcon("screen"),
                    Build = () => screen = new ScreenSettingsPanel(),
                    // Windows moves the brightness without this application hearing anything, and the
                    // session moves the two levers.
                    Enter = () => screen?.Reload(),
                    // A slider position the debounce is still holding would otherwise be lost.
                    Leave = () => screen?.Flush(),
                },
                new SettingsSection
                {
                    Tag = MqttTag, Label = "MQTT",
                    Icon = NavIcon("mqtt"),
                    // Built once, as the shared panel requires: it is initialised in the page's
                    // constructor and never again.
                    Build = () => mqtt = new MqttSettingsPage(),
                    // The link comes and goes on its own. Refresh rather than Reload: nothing outside
                    // the panel writes its settings file.
                    Enter = () => mqtt?.Refresh(),
                    // No Leave hook. Cancel is final and belongs to the window closing, below.
                },
                new SettingsSection
                {
                    Tag = AppearanceTag, Label = AppText.Get("SettingsAppearancePage"),
                    Icon = NavIcon("appearance"),
                    Build = () => appearance = new AppearanceSettingsPanel(),
                    // settings.json roams, so the stored wish can arrive from another machine while
                    // the page is open.
                    Enter = () => appearance?.Reload(),
                },
                new SettingsSection
                {
                    Tag = AboutTag, Label = AppText.Get("SettingsAboutPage"),
                    Icon = NavIcon("about"),
                    // Built once: the payload is fixed for the run, and the button follows the shared
                    // check from the moment the page exists rather than from when it is looked at.
                    Build = () => about = new AboutSettingsPanel(),
                },
                new SettingsSection
                {
                    Tag = DiagnosticsTag, Label = AppText.Get("SettingsDiagnosticsPage"),
                    Icon = NavIcon("diagnostics"),
                    Build = () => diagnostics = new DiagnosticsSettingsPanel(),
                    // The log folder gains files while the window is open.
                    Enter = () => diagnostics?.Reload(),
                },
            ],
            InitialTag     = tag,
            // FocusDesk follows the system light/dark setting rather than pinning one. The shell
            // paints the caption strip from this same value, so the title bar cannot end up light
            // over a dark page.
            Theme          = ElementTheme.Default,
            BackdropTint   = BackdropTint,
            RectStore      = rectStore,
            ProductMark    = new SvgImageSource(new Uri("ms-appx:///Assets/mark.svg")),
            ProductName    = AppInfo.Name,
            ProductVersion = $"v{AppInfo.Version}",
            PageMaxWidth   = 720,
        });
        AppTitleBar.Apply(window);

        // A session starts and ends on its own clock and from Home Assistant, so the controls it
        // greys follow it for as long as the window is open. Pages not built yet read the lock as
        // they are built.
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        void FollowSession() => dispatcher.TryEnqueue(() =>
        {
            programFocus?.ApplySessionLock();
            screen?.ApplySessionLock();
            about?.ApplySessionLock();
            diagnostics?.ApplySessionLock();
        });
        FocusSessionService.Changed += FollowSession;

        _window = window;
        window.Closed += (_, _) =>
        {
            _window = null;
            FocusSessionService.Changed -= FollowSession;
            // A probe started from the MQTT page outlives the window, and the panel must not be
            // touched again once this has run.
            mqtt?.Cancel();
            // The update coordinator lives for the process, so a page left subscribed stays
            // reachable and keeps touching a torn-down tree.
            about?.Detach();
        };
        // A window moved onto a display at another scale resizes itself by the ratio; placed again, it stays.
        if (rectStore.Opened is { } opened)
            window.AppWindow.MoveAndResize(new RectInt32(opened.X, opened.Y, opened.Width, opened.Height));
        window.Activate();
    }

    /// <summary>The colour behind the Settings window, one per theme: FocusDesk's own purple-grey
    /// ground, the one the caption strip is painted in and the program picker is tinted with, rather
    /// than the colour Windows mixes from the wallpaper.</summary>
    internal static BackdropTint BackdropTint { get; } = new()
    {
        Light = Colour(AppPalette.GroundTint(isDark: false)),
        Dark  = Colour(AppPalette.GroundTint(isDark: true)),
    };

    private static BackdropColour Colour(uint argb) =>
        new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>A pane entry's artwork, by the file name under <c>Assets\nav\</c>. Two-tone, so it
    /// goes through an image rather than a path icon, which carries one colour only.</summary>
    private static IconSource NavIcon(string name) => new ImageIconSource
    {
        ImageSource = new SvgImageSource(new Uri($"ms-appx:///Assets/nav/{name}.svg")),
    };
}

/// <summary>The Settings window's spot, kept in the settings document; what the shell gets is always on a display.</summary>
internal sealed class SettingsWindowRectStore : IWindowRectStore
{
    /// <summary>The rectangle last handed to the shell.</summary>
    public WindowRect? Opened { get; private set; }

    public WindowRect? Load()
    {
        var displays = DisplayAreas.All();
        var rect = SettingsWindowPlacement.Opening(
            SettingsService.Read(SpotIn), displays, DisplayAreas.Home(displays, TrayIconHost.ShellId));
        Opened = new WindowRect(rect.Left, rect.Top, rect.Width, rect.Height);
        return Opened;
    }

    public void Save(WindowRect rect)
    {
        var spot = SettingsWindowPlacement.Keep(
            new NativeRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height), DisplayAreas.All());
        SettingsService.Update(s => Store(s, spot));
    }

    /// <summary>The spot one settings object carries, or null unless all four values are there.</summary>
    internal static SettingsWindowSpot? SpotIn(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s is { SettingsWindowX: { } x, SettingsWindowY: { } y,
                      SettingsWindowWidth: { } width, SettingsWindowHeight: { } height }
            ? new SettingsWindowSpot(x, y, width, height)
            : null;
    }

    /// <summary>Puts a spot into one settings object, apart from <see cref="Save"/> for the round-trip test.</summary>
    internal static void Store(AppSettings s, SettingsWindowSpot spot)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.SettingsWindowX      = spot.X;
        s.SettingsWindowY      = spot.Y;
        s.SettingsWindowWidth  = spot.WidthInUnits;
        s.SettingsWindowHeight = spot.HeightInUnits;
    }
}
