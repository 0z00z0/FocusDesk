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

    [Theory]
    [InlineData("#d8a65")]
    [InlineData("#d8a6577")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void AColourThatIsNotSixHexDigitsIsRefused(string text) =>
        Assert.Throws<FormatException>(() => AppColors.FromHex(text));
}
