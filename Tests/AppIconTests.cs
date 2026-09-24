using Xunit;
using ZeroZero.Tray;

namespace FocusDesk.Tests;

/// <summary>
/// The icon files are drawn once by a script and committed, so nothing at build time would notice
/// one that is malformed. A tray icon that will not decode is a failure with nothing on screen to
/// explain it: the notification area shows a blank slot, or the host throws on start and the
/// application runs with no icon at all.
/// </summary>
/// <remarks>The form of a frame is the trap this exists for. A frame written as PNG is read by the
/// shell at every size, but comes back as noise through <c>System.Drawing.Icon</c>, which is what
/// the tray host, the icon compiler and Inno Setup all reach for — so every frame below 256 pixels
/// is a device-independent bitmap and only the 256 frame is PNG.</remarks>
public class AppIconTests
{
    /// <summary>The application mark, carried by the executable and by the installer.</summary>
    private const string ApplicationIcon = @"Assets\FocusDesk.ico";

    /// <summary>The notification-area icons, one per taskbar theme.</summary>
    public static TheoryData<string> TrayIcons =>
        [@"Assets\FocusDeskTray.ico", @"Assets\FocusDeskTrayLight.ico"];

    public static TheoryData<string> AllIcons =>
        [ApplicationIcon, @"Assets\FocusDeskTray.ico", @"Assets\FocusDeskTrayLight.ico"];

    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryFrameSitsInsideTheFileAndDeclaresItsOwnSize(string relativePath)
    {
        var frames = IconFrames.Read(RepoFiles.Find(relativePath));

        Assert.NotEmpty(frames);
        foreach (var frame in frames)
        {
            Assert.Equal(frame.DeclaredSide, frame.ActualSide);
            Assert.True(frame.EndsInsideTheFile,
                $"A frame of {frame.DeclaredSide} px in '{relativePath}' runs past the end of the file.");
        }
    }

    /// <summary>The one that has already fired: a tray frame written as PNG decodes as noise.</summary>
    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryFrameBelowTwoHundredAndFiftySixIsABitmapRatherThanAPng(string relativePath)
    {
        foreach (var frame in IconFrames.Read(RepoFiles.Find(relativePath)))
            Assert.Equal(frame.DeclaredSide >= 256, frame.IsPng);
    }

    /// <summary>
    /// Every slot the taskbar asks for from 100 % to 200 % has a frame of its own size. A size the
    /// file does not carry is resampled by the shell, and a thin ring resampled comes out soft.
    /// </summary>
    [Theory]
    [MemberData(nameof(TrayIcons))]
    public void TheTraySlotsFromOneHundredToTwoHundredPercentEachHaveTheirOwnFrame(string relativePath)
    {
        var sides = IconFrames.Read(RepoFiles.Find(relativePath)).Select(f => f.DeclaredSide).ToHashSet();

        foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            Assert.Contains(TrayIconSlot.PixelsFor(scale), sides);
    }

    /// <summary>The executable and the installer carry the same mark, and the file they name
    /// exists. A rename that drops one of the two ships an application with no icon.</summary>
    [Fact]
    public void TheExecutableAndTheInstallerBothCarryTheApplicationMark()
    {
        Assert.Contains($"<ApplicationIcon>{ApplicationIcon}</ApplicationIcon>",
                        RepoFiles.Read("FocusDesk.csproj"), StringComparison.Ordinal);

        // The installer script sits one folder down, so it names the same file relatively.
        Assert.Contains($@"SetupIconFile=..\{ApplicationIcon}",
                        RepoFiles.Read(@"installer\FocusDesk.iss"), StringComparison.Ordinal);

        RepoFiles.Find(ApplicationIcon);
    }
}

/// <summary>One frame of an icon file, as its directory entry and its own header describe it.</summary>
/// <param name="DeclaredSide">The side the directory entry claims, with 0 read as 256.</param>
/// <param name="ActualSide">The side the frame's own header carries.</param>
internal readonly record struct IconFrame(
    int DeclaredSide, int ActualSide, bool IsPng, bool EndsInsideTheFile);

/// <summary>Reads an icon file's directory without decoding anything, so the check runs anywhere
/// and says which frame is wrong rather than only that the file would not load.</summary>
internal static class IconFrames
{
    public static IReadOnlyList<IconFrame> Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);

        Assert.True(bytes.Length > 6, $"'{path}' is too short to hold an icon directory.");
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));   // type 1 is an icon, 2 a cursor

        int count = BitConverter.ToUInt16(bytes, 4);
        var frames = new List<IconFrame>(count);

        for (int i = 0; i < count; i++)
        {
            int entry = 6 + (16 * i);
            int declared = bytes[entry] == 0 ? 256 : bytes[entry];
            int length = (int)BitConverter.ToUInt32(bytes, entry + 8);
            int offset = (int)BitConverter.ToUInt32(bytes, entry + 12);

            bool inside = offset > 0 && length > 0 && offset + length <= bytes.Length;
            if (!inside)
            {
                frames.Add(new IconFrame(declared, -1, false, false));
                continue;
            }

            // A PNG opens with its signature; anything else here is a bitmap header, whose width
            // sits at byte 4 and whose height is doubled to cover the mask below the colour rows.
            bool isPng = bytes[offset] == 0x89 && bytes[offset + 1] == 0x50;
            int actual = isPng
                ? (bytes[offset + 16] << 24) | (bytes[offset + 17] << 16) | (bytes[offset + 18] << 8) | bytes[offset + 19]
                : BitConverter.ToInt32(bytes, offset + 4);

            frames.Add(new IconFrame(declared, actual, isPng, true));
        }

        return frames;
    }
}
