using FocusDesk.Helpers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Switching the icon out of the overflow flyout and putting back exactly what was there. The value
/// being written belongs to the shell, so leaving a zero where the shell held nothing is a change to
/// somebody else's setting that nothing would ever undo.
/// </summary>
public class TrayIconPromotionTests
{
    private static readonly Guid Icon = Guid.Parse("ADAB45AC-EC29-6860-C480-EDA4F569A006");
    private static readonly Guid Other = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void SwitchingOffWhereTheShellHeldNothingDeletesRatherThanWritingAZero()
    {
        var store = new FakeStore();   // no value at all

        var record = TrayIconPromotion.TurnOn(store, Icon, existing: null);
        Assert.True(store.Promoted);

        Assert.Null(TrayIconPromotion.TurnOff(store, Icon, record));
        Assert.True(store.Cleared);
        Assert.Null(store.Promoted);
    }

    [Fact]
    public void SwitchingOffPutsBackAValueTheShellAlreadyHeld()
    {
        var store = new FakeStore { Promoted = false };

        var record = TrayIconPromotion.TurnOn(store, Icon, existing: null);
        Assert.True(store.Promoted);

        Assert.Null(TrayIconPromotion.TurnOff(store, Icon, record));
        Assert.False(store.Cleared);
        Assert.False(store.Promoted);
    }

    /// <summary>Switching on twice must not overwrite the record with the value the first switch
    /// wrote, or the restore puts back the promotion instead of what preceded it.</summary>
    [Fact]
    public void TheFirstValueSeenIsTheOneKept()
    {
        var store = new FakeStore { Promoted = false };

        var first = TrayIconPromotion.TurnOn(store, Icon, existing: null);
        var again = TrayIconPromotion.TurnOn(store, Icon, first);

        Assert.Equal(first, again);
        Assert.Equal(false, again!.Value.Promoted);
    }

    /// <summary>A record from another icon is not this icon's to spend. It reaches here through a
    /// settings document that roams between machines.</summary>
    [Fact]
    public void ARecordForAnotherIconIsLeftAlone()
    {
        var store = new FakeStore { Promoted = true };
        var stranger = new TrayPromotionRecord(Other, Promoted: false);

        Assert.Equal(stranger, TrayIconPromotion.TurnOff(store, Icon, stranger));
        Assert.True(store.Promoted);
        Assert.False(store.Cleared);
    }

    /// <summary>A write that does not land leaves the record owed, so the next attempt still knows
    /// what to put back.</summary>
    [Fact]
    public void AFailedRestoreKeepsTheRecord()
    {
        var store = new FakeStore { Promoted = false };
        var record = TrayIconPromotion.TurnOn(store, Icon, existing: null);

        store.Refuse = true;
        Assert.Equal(record, TrayIconPromotion.TurnOff(store, Icon, record));
    }

    /// <summary>A value the shell holds is left alone at startup, zero included: a person dragging
    /// the icon into the overflow writes that zero, and overriding it on every start would take the
    /// choice away. Only a value that has gone is put back.</summary>
    [Fact]
    public void StartupPutsBackOnlyAValueThatHasGone()
    {
        var demoted = new FakeStore { Promoted = false };
        TrayIconPromotion.Reapply(demoted, Icon, wanted: true, existing: null, out bool touched);
        Assert.False(touched);
        Assert.False(demoted.Promoted);

        var gone = new FakeStore();
        var held = new TrayPromotionRecord(Icon, Promoted: false);
        var kept = TrayIconPromotion.Reapply(gone, Icon, wanted: true, held, out bool applied);
        Assert.True(applied);
        Assert.True(gone.Promoted);
        Assert.Equal(held, kept);

        var off = new FakeStore();
        TrayIconPromotion.Reapply(off, Icon, wanted: false, existing: null, out bool untouched);
        Assert.False(untouched);
        Assert.Null(off.Promoted);
    }

    private sealed class FakeStore : ITrayPromotionStore
    {
        public bool? Promoted { get; set; }
        public bool Cleared { get; private set; }
        public bool Refuse { get; set; }

        public bool? Read(Guid icon) => Promoted;

        public bool Write(Guid icon, bool promoted)
        {
            if (Refuse) return false;
            Promoted = promoted;
            Cleared  = false;
            return true;
        }

        public bool Clear(Guid icon)
        {
            if (Refuse) return false;
            Promoted = null;
            Cleared  = true;
            return true;
        }
    }
}
