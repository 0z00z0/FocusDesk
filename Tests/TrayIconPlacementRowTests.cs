using FocusDesk.Helpers;
using Xunit;
using ZeroZero.Tray;

namespace FocusDesk.Tests;

/// <summary>
/// The Appearance row moving the icon beside the clock and putting back where it was. The setting
/// written belongs to the person using the machine, so the row may only ever undo its own write.
/// </summary>
public class TrayIconPlacementRowTests
{
    private static readonly Guid Icon  = Guid.Parse("ADAB45AC-EC29-6860-C480-EDA4F569A006");
    private static readonly Guid Other = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void SwitchingOffPutsBackWhereTheIconWas()
    {
        var tray = new FakeTray(Icon, TrayIconPlacement.Overflow);

        var record = TrayIconPlacementRow.TurnOn(tray, held: null);
        Assert.Equal(TrayIconPlacement.NotificationArea, tray.Placement);

        Assert.Null(TrayIconPlacementRow.TurnOff(tray, record));
        Assert.Equal(TrayIconPlacement.Overflow, tray.Placement);
    }

    /// <summary>A record from another identity is not this icon's to spend. It reaches here through a
    /// settings document that roams between machines, or from before the icon's identity moved.</summary>
    [Fact]
    public void ARecordForAnotherIconWritesNothing()
    {
        var tray = new FakeTray(Icon, TrayIconPlacement.NotificationArea);
        var stranger = new TrayPlacementRecord(Other, TrayIconPlacement.Overflow);

        Assert.Equal(stranger, TrayIconPlacementRow.TurnOff(tray, stranger));
        Assert.Equal(0, tray.Asks);
        Assert.Equal(TrayIconPlacement.NotificationArea, tray.Placement);
    }

    /// <summary>A start that writes again, after the shell lost the setting, must not replace the
    /// record with the overflow it found, or switching off moves the icon away from where it began.</summary>
    [Fact]
    public void TheFirstPlacementSeenIsKept()
    {
        var tray = new FakeTray(Icon, TrayIconPlacement.Overflow);
        var held = new TrayPlacementRecord(Icon, TrayIconPlacement.NotificationArea);

        Assert.Equal(held, TrayIconPlacementRow.TurnOn(tray, held));
        Assert.Equal(TrayIconPlacement.NotificationArea, tray.Placement);
    }

    /// <summary>Writes the way the shared host does: only where the setting does not already read as
    /// wanted.</summary>
    private sealed class FakeTray(Guid shellId, TrayIconPlacement placement) : ITrayPlacement
    {
        public Guid? ShellId { get; } = shellId;
        public TrayIconPlacement? Placement { get; private set; } = placement;
        public int Asks { get; private set; }

        public bool AskFor(TrayIconPlacement wanted)
        {
            Asks++;
            if (Placement == wanted) return false;
            Placement = wanted;
            return true;
        }
    }
}
