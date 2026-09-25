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
/// carry the product's colour rather than the operating system's.
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
    internal static uint SecondaryTextTint(bool isDark)
    {
        Color purple = AppColors.FromHex(Brand.ColorPurple);
        OklabColor hue = Oklab.FromArgb(((uint)purple.R << 16) | ((uint)purple.G << 8) | purple.B);

        double chroma = isDark ? DarkTintChroma : LightTintChroma;
        double scale = chroma / Math.Sqrt(hue.A * hue.A + hue.B * hue.B);
        double lightness = isDark ? DarkTintLightness : LightTintLightness;

        return Oklab.ToArgb(new OklabColor(lightness, hue.A * scale, hue.B * scale), 0xFF);
    }

    // The system background colour is black on the dark theme and white on the light one.
    private static Color SecondaryTextColour(UISettings systemColours) => AppColors.FromPacked(
        SecondaryTextTint(systemColours.GetColorValue(UIColorType.Background).R < 128));
}
