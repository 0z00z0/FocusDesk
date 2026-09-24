using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using ZeroZero.Brand.Core;

namespace FocusDesk.Helpers;

/// <summary>
/// The studio accent over the stock WinUI keys. Amber is the accent, so an accent button, a selected
/// pane entry and a focus ring all carry the product's colour rather than the operating system's.
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
    /// <summary>Puts the accent keys into a dictionary. Called with the application's own resources
    /// before any window is built; a control resolves a key once, when it is created.</summary>
    public static void Apply(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        Color amber = AppColors.FromHex(Brand.ColorAmber);

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
    }
}
