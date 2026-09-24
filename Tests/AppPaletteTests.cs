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

    [Theory]
    [InlineData("#d8a65")]
    [InlineData("#d8a6577")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void AColourThatIsNotSixHexDigitsIsRefused(string text) =>
        Assert.Throws<FormatException>(() => AppColors.FromHex(text));
}
