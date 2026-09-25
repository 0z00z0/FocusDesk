using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using ZeroZero.SettingsShell.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// The Settings window: the shared shell, holding this application's two pages. One window at a
/// time — a second request brings the open one forward rather than opening another.
/// </summary>
/// <remarks>The shell owns the window, the navigation pane, the placement and Escape; everything
/// inside a page is this application's.</remarks>
internal static class SettingsShellHost
{
    public const string FocusTag  = "focus";
    public const string ScreenTag = "screen";
    public const string MqttTag       = "mqtt";
    public const string AppearanceTag = "appearance";

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
        ScreenSettingsPanel? screen = null;
        MqttSettingsPage? mqtt = null;
        AppearanceSettingsPanel? appearance = null;

        var rectStore = new SettingsWindowRectStore();
        // Only a first open is sized to its content: fitting a remembered rectangle would grow the
        // window back every time, which is the same as not remembering a size at all.
        bool fitToContent = rectStore.Load() is null;

        var window = new SettingsWindow(new SettingsWindowSetup
        {
            Title = $"{AppInfo.Name} settings",
            Sections =
            [
                new SettingsSection
                {
                    Tag = FocusTag, Label = "Focus",
                    Icon = NavIcon("focus"),
                    Build = () => focus = new FocusSettingsPanel(),
                    // The session moves on its own clock, so the page follows it only while it is
                    // the one on screen.
                    Enter = () => focus?.Watch(),
                    Leave = () => focus?.Unwatch(),
                },
                new SettingsSection
                {
                    Tag = ScreenTag, Label = "Screen",
                    Icon = NavIcon("screen"),
                    Build = () => screen = new ScreenSettingsPanel(),
                    // Windows moves the brightness without this application hearing anything.
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
                    Tag = AppearanceTag, Label = "Appearance",
                    Icon = NavIcon("appearance"),
                    Build = () => appearance = new AppearanceSettingsPanel(),
                    // settings.json roams, so the stored wish can arrive from another machine while
                    // the page is open.
                    Enter = () => appearance?.Reload(),
                },
            ],
            InitialTag     = tag,
            // FocusDesk follows the system light/dark setting rather than pinning one. The shell
            // paints the caption strip from this same value, so the title bar cannot end up light
            // over a dark page.
            Theme          = ElementTheme.Default,
            RectStore      = rectStore,
            ProductMark    = new SvgImageSource(new Uri("ms-appx:///Assets/mark.svg")),
            ProductName    = AppInfo.Name,
            ProductVersion = AppInfo.Version,
            PageMaxWidth   = 720,
        });

        _window = window;
        window.Closed += (_, _) =>
        {
            _window = null;
            // A probe started from the MQTT page outlives the window, and the panel must not be
            // touched again once this has run.
            mqtt?.Cancel();
        };
        // Straight after the constructor, as the shell requires: it waits for load if it must.
        if (fitToContent) window.FitToPages();
        window.Activate();
    }

    /// <summary>A pane entry's artwork, by the file name under <c>Assets\nav\</c>. Two-tone, so it
    /// goes through an image rather than a path icon, which carries one colour only.</summary>
    private static IconSource NavIcon(string name) => new ImageIconSource
    {
        ImageSource = new SvgImageSource(new Uri($"ms-appx:///Assets/nav/{name}.svg")),
    };
}

/// <summary>Where the Settings window's rectangle is kept: the application's own settings document,
/// in its Window section.</summary>
internal sealed class SettingsWindowRectStore : IWindowRectStore
{
    public WindowRect? Load() => SettingsService.Read(RectIn);

    public void Save(WindowRect rect) => SettingsService.Update(s => Store(s, rect));

    /// <summary>The rectangle one settings object carries, or null where it carries none. All four
    /// values are needed: a document holding three of them describes no rectangle, so the window
    /// opens where it would with nothing saved.</summary>
    internal static WindowRect? RectIn(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s is { SettingsWindowX: { } x, SettingsWindowY: { } y,
                      SettingsWindowWidth: { } width, SettingsWindowHeight: { } height }
            ? new WindowRect(x, y, width, height)
            : null;
    }

    /// <summary>Puts a rectangle into one settings object. Separated from <see cref="Save"/> so the
    /// round trip through the document can be exercised without the installed settings file.</summary>
    internal static void Store(AppSettings s, WindowRect rect)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.SettingsWindowX      = rect.X;
        s.SettingsWindowY      = rect.Y;
        s.SettingsWindowWidth  = rect.Width;
        s.SettingsWindowHeight = rect.Height;
    }
}
