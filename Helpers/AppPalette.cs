using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;
using ZeroZero.Brand.Core;

namespace FocusDesk.Helpers;

/// <summary>
/// The studio look over the stock WinUI keys: the amber accent, the brand face, a card outline and a
/// muted purple tint for secondary text. An accent button, a selected pane entry and a focus ring all
/// carry the product's colour rather than the operating system's. The dark purple-hued tones the
/// Settings window's ground, its caption and the About header are painted in come from here too.
/// </summary>
/// <remarks>
/// <para>Applied in code rather than declared in <c>App.xaml</c>: a brush declared in the
/// application's own dictionary cannot take its colour from a key inside a merged dictionary's theme
/// dictionaries, and a theme dictionary of the application's own does not override a stock key at
/// all. The values come from the brand constants, which a shared test holds to the same dictionary
/// the markup merges, so the two cannot drift.</para>
/// <para>Text on the amber accent is black. Amber clears 9.5:1 against black and reaches 2.2:1
/// against white, and the stock key is white.</para>
/// </remarks>
internal static class AppPalette
{
    // Oklab lightness and chroma of the secondary-text tint, on purple's hue. Dark measures 7.5:1 on
    // a card over Mica, light 7.0:1; AppPaletteTests holds both above 4.5:1 on the worst ground.
    private const double DarkTintLightness = 0.80, DarkTintChroma = 0.04;
    private const double LightTintLightness = 0.46, LightTintChroma = 0.06;

    // The window ground, on purple's hue at the lightness of the platform's own caption on each
    // theme, so the caption strip and the page meet without a seam. AppPaletteTests holds the text on
    // both above 4.5:1.
    private const double DarkGroundLightness = 0.235, LightGroundLightness = 0.96;
    private const double DarkGroundChroma = 0.02, LightGroundChroma = 0.008;

    // The shared About control's header block, dark on both themes: its text is fixed white. The
    // same lightness as the studio navy it replaces, so the version line keeps its 4.5:1.
    private const double HeaderTopLightness = 0.27, HeaderTopChroma = 0.035;
    private const double HeaderBottomLightness = 0.16, HeaderBottomChroma = 0.02;

    // Kept for the life of the process: the theme-change event is lost with the object.
    private static UISettings? _systemColours;

    /// <summary>Puts the studio keys into a dictionary. Called with the application's own resources
    /// before any window is built; a control resolves a key once, when it is created.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        Color amber = AppColors.FromHex(Brand.ColorAmber);
        Color purple = AppColors.FromHex(Brand.ColorPurple);

        // Several control visual-states resolve the accent COLOUR rather than the AccentFill brush,
        // and would paint the operating system's accent without these three.
        resources["SystemAccentColor"] = amber;
        resources["AccentFillColorDefaultColor"] = amber;
        resources["AccentTextFillColorPrimaryColor"] = amber;

        resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(amber);
        resources["AccentFillColorSecondaryBrush"] = new SolidColorBrush(amber) { Opacity = 0.9 };
        resources["AccentFillColorTertiaryBrush"] = new SolidColorBrush(amber) { Opacity = 0.8 };
        resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(amber);

        var onAccent = new SolidColorBrush(AppColors.FromPacked(0xFF000000));
        resources["TextOnAccentFillColorPrimaryBrush"] = onAccent;
        resources["TextOnAccentFillColorSecondaryBrush"] = onAccent;

        // The toolkit's settings card draws its outline from this key; the stock value measures
        // 1.06:1 against a dark card. Translucent, so one value reads on both themes.
        resources["CardStrokeColorDefaultBrush"] = new SolidColorBrush(purple) { Opacity = 0.3 };

        // One brush whose colour follows the theme, so every control holding it repaints without
        // resolving the key again.
        _systemColours = new UISettings();
        var secondaryText = new SolidColorBrush(SecondaryTextColour(_systemColours));
        resources["TextFillColorSecondaryBrush"] = secondaryText;
        DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();
        _systemColours.ColorValuesChanged += (sender, _) => queue.TryEnqueue(() =>
            secondaryText.Color = SecondaryTextColour(sender));

        // Templated controls resolve these two keys rather than the implicit TextBlock style in
        // App.xaml; Cascadia Mono renders larger than the stock face, so the size drops a notch.
        object brandFont = resources["BrandFontFamily"];
        resources["ContentControlThemeFontFamily"] = brandFont;
        resources["ControlContentThemeFontFamily"] = brandFont;
        resources["ControlContentThemeFontSize"] = 12.5;

        // The shared MQTT panel resolves its own keys rather than the ones above. Its accent falls
        // back to SystemAccentColorLight2 and its secondary text to the stock colour, so without these
        // that page alone carries the operating system's accent and neutral grey. Its body and card
        // keys are left alone: they follow the stock light and dark theme.
        resources["MqttPanelAccentBrush"] = new SolidColorBrush(amber);
        resources["MqttPanelHeadingBrush"] = new SolidColorBrush(purple);
        resources["MqttPanelSecondaryBrush"] = secondaryText;
    }

    /// <summary>Packed 0xAARRGGBB for secondary text: a low-chroma tint on the studio purple's hue,
    /// light on the dark theme and dark on the light one.</summary>
    internal static uint SecondaryTextTint(bool isDark) => isDark
        ? PurpleTone(DarkTintLightness, DarkTintChroma)
        : PurpleTone(LightTintLightness, LightTintChroma);

    /// <summary>Packed 0xAARRGGBB for the window ground behind the Settings pages: a near-neutral on
    /// the studio purple's hue, dark on the dark theme and light on the light one.</summary>
    internal static uint GroundTint(bool isDark) => isDark
        ? PurpleTone(DarkGroundLightness, DarkGroundChroma)
        : PurpleTone(LightGroundLightness, LightGroundChroma);

    /// <summary>The dark caption over <see cref="GroundTint"/>: the ground itself, and a step lighter
    /// for a hovered and a pressed caption button, as the platform's own dark caption steps.</summary>
    internal static (uint Ground, uint Hover, uint Pressed) DarkCaption => (
        PurpleTone(DarkGroundLightness, DarkGroundChroma),
        PurpleTone(DarkGroundLightness + 0.04, DarkGroundChroma),
        PurpleTone(DarkGroundLightness + 0.025, DarkGroundChroma));

    /// <summary>The About header block's gradient, top-left to bottom-right.</summary>
    internal static (uint Top, uint Bottom) AboutHeader => (
        PurpleTone(HeaderTopLightness, HeaderTopChroma),
        PurpleTone(HeaderBottomLightness, HeaderBottomChroma));

    /// <summary>An opaque Oklab tone of the given lightness and chroma on the studio purple's hue.</summary>
    private static uint PurpleTone(double lightness, double chroma)
    {
        Color purple = AppColors.FromHex(Brand.ColorPurple);
        OklabColor hue = Oklab.FromArgb(((uint)purple.R << 16) | ((uint)purple.G << 8) | purple.B);

        double scale = chroma / Math.Sqrt(hue.A * hue.A + hue.B * hue.B);
        return Oklab.ToArgb(new OklabColor(lightness, hue.A * scale, hue.B * scale), 0xFF);
    }

    // The system background colour is black on the dark theme and white on the light one.
    private static Color SecondaryTextColour(UISettings systemColours) => AppColors.FromPacked(
        SecondaryTextTint(systemColours.GetColorValue(UIColorType.Background).R < 128));
}
