using FocusDesk.Helpers;
using Xunit;
using ZeroZero.Brand.Core;

namespace FocusDesk.Tests;

/// <summary>
/// The application palette. A lost merge or a mis-parsed constant costs nothing at build time and
/// nothing at run time: the window simply paints the operating system's blue again, which is the
/// kind of regression that ships unnoticed.
/// </summary>
public class AppPaletteTests
{
    private static readonly string AppMarkup = RepoFiles.Read("App.xaml");
    private static readonly string AppCode = RepoFiles.Read("App.xaml.cs");

    /// <summary>The application's constructor, from its signature to the next member.</summary>
    private static string Constructor
    {
        get
        {
            int start = AppCode.IndexOf("public App()", StringComparison.Ordinal);
            int end = AppCode.IndexOf("protected override void OnLaunched", StringComparison.Ordinal);
            Assert.InRange(start, 0, end);
            return AppCode[start..end];
        }
    }

    /// <summary>Reading <c>Application.Resources</c> from the constructor fails with E_UNEXPECTED
    /// while the initialisation callback runs: Microsoft.UI.Xaml stows the failure and ends the
    /// process at 0xC000027B, before the crash arms are registered and with nothing written to the
    /// log. The application never reaches its notification-area icon.</summary>
    [Fact]
    public void TheAccentIsNotAppliedFromTheConstructor()
    {
        Assert.DoesNotContain("Resources", Constructor, StringComparison.Ordinal);
        Assert.Contains("AppPalette.Apply(Resources);", AppCode, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBrandDictionaryIsMerged() =>
        Assert.Contains("ms-appx:///ZeroZero.Brand.WinUI/Themes/BrandResources.xaml", AppMarkup,
            StringComparison.Ordinal);

    /// <summary>The studio colours arrive from the brand component. A hex literal in the markup is a
    /// second copy of a value that already has one home, and the two drift apart silently.</summary>
    [Fact]
    public void NoColourIsWrittenAsAHexLiteral() =>
        Assert.DoesNotContain("\"#", AppMarkup, StringComparison.Ordinal);

    [Fact]
    public void TheAccentIsTheStudioAmber()
    {
        var amber = AppColors.FromHex(Brand.ColorAmber);
        Assert.Equal(0xFF, amber.A);
        Assert.Equal(0xD8, amber.R);
        Assert.Equal(0xA6, amber.G);
        Assert.Equal(0x57, amber.B);
    }

    /// <summary>Card descriptions are body text, so the tint holds the WCAG 4.5:1 floor on the worst
    /// ground each theme gives it: a hovered card over Mica on dark, and a Mica-tinted card on light.
    /// </summary>
    [Theory]
    [InlineData(true, 0xFF333333u)]
    [InlineData(false, 0xFFE8E8E8u)]
    public void SecondaryTextClearsTheBodyTextFloor(bool isDark, uint ground) =>
        Assert.True(Contrast(AppPalette.SecondaryTextTint(isDark), ground) >= 4.5);

    /// <summary>The Settings window's ground is what every page's text sits on, and the caption strip
    /// is painted the same. Primary and secondary text each hold 4.5:1 on the ground, and secondary
    /// text on a hovered card over it (eight per cent white on dark, the light card fill on light).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TextOnTheSettingsGroundClearsTheBodyTextFloor(bool isDark)
    {
        uint ground = AppPalette.GroundTint(isDark);
        uint primary = isDark ? 0xFFFFFFFFu : Over(0xE4000000u, ground);
        uint card = isDark ? Over(0x14FFFFFFu, ground) : Over(0xB3FFFFFFu, ground);
        uint secondary = AppPalette.SecondaryTextTint(isDark);

        Assert.True(Contrast(primary, ground) >= 4.5);
        Assert.True(Contrast(secondary, ground) >= 4.5);
        Assert.True(Contrast(secondary, card) >= 4.5);
    }

    /// <summary>The shared About control writes its product name in white and its version in
    /// half-transparent white over the header block; both hold 4.5:1 at each end of the gradient.</summary>
    [Fact]
    public void TheAboutHeaderTextClearsTheBodyTextFloor()
    {
        var (top, bottom) = AppPalette.AboutHeader;
        foreach (uint stop in new[] { top, bottom })
        {
            Assert.True(Contrast(0xFFFFFFFFu, stop) >= 4.5);
            Assert.True(Contrast(Over(0x80FFFFFFu, stop), stop) >= 4.5);
        }
    }

    /// <summary>A translucent colour composited over an opaque ground, channel by channel.</summary>
    private static uint Over(uint argb, uint ground)
    {
        double alpha = (argb >> 24) / 255.0;
        uint Channel(int shift) => (uint)Math.Round(
            ((argb >> shift) & 0xFF) * alpha + ((ground >> shift) & 0xFF) * (1 - alpha));
        return 0xFF000000u | (Channel(16) << 16) | (Channel(8) << 8) | Channel(0);
    }

    private static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(uint argb)
    {
        static double Linear(uint channel)
        {
            double c = (channel & 0xFF) / 255.0;
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(argb >> 16) + 0.7152 * Linear(argb >> 8) + 0.0722 * Linear(argb);
    }

    [Theory]
    [InlineData("#d8a65")]
    [InlineData("#d8a6577")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void AColourThatIsNotSixHexDigitsIsRefused(string text) =>
        Assert.Throws<FormatException>(() => AppColors.FromHex(text));
}
