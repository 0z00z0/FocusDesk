using System;
using System.IO;
using FocusDesk.Services;
using FocusDesk.UI;
using Xunit;
using ZeroZero.SettingsShell.WinUI;

namespace FocusDesk.Tests;

/// <summary>
/// Where the Settings window reopens. The rectangle goes out through the settings document and comes
/// back through it, so a value that never reached the file, or came back as another number, leaves
/// the window somewhere the person did not put it.
/// </summary>
public class SettingsWindowRectStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"focusdesk-rect-test-{Guid.NewGuid():N}");

    private string File_ => Path.Combine(_dir, "settings.json");

    public SettingsWindowRectStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ARectangleSurvivesTheTripThroughTheDocument()
    {
        // Negative coordinates on purpose: a monitor to the left of the primary one is where a
        // rectangle written as an unsigned value would come back wrong.
        var saved = new WindowRect(-1720, 240, 980, 700);

        var written = new AppSettings();
        SettingsWindowRectStore.Store(written, saved);
        Assert.True(SettingsService.WriteTo(written, File_));

        var read = SettingsService.ReadFrom(File_);

        Assert.NotNull(read);
        Assert.Equal(saved, SettingsWindowRectStore.RectIn(read!));
    }

    [Fact]
    public void ADocumentThatHasNeverHeldTheWindowYieldsNothing()
    {
        Assert.True(SettingsService.WriteTo(new AppSettings(), File_));

        var read = SettingsService.ReadFrom(File_);

        Assert.NotNull(read);
        Assert.Null(SettingsWindowRectStore.RectIn(read!));
    }

    [Fact]
    public void ARectangleMissingOneOfItsFourValuesYieldsNothing() =>
        // Not three-quarters of a rectangle: the window opens centred on the cursor's monitor
        // instead, which is where it opens with nothing saved at all.
        Assert.Null(SettingsWindowRectStore.RectIn(new AppSettings
        {
            SettingsWindowX = 100, SettingsWindowY = 200, SettingsWindowWidth = 980,
        }));
}
