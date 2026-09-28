using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Which levers each kind of session holds, for every setting of the three switches. The whole
/// two-kind design rests on this table.
/// </summary>
/// <remarks>The expected levers are written out row by row rather than derived: a test computing them
/// the way the plan does would follow a changed rule instead of catching it.</remarks>
public class FocusSessionPlanTests
{
    // program focus (else screen break), network switch, dim switch, input switch
    //   → limits, covers, blocks input, dims, blocks network
    [Theory]
    [InlineData(true,  false, false, false, true, false, false, false, false)]
    [InlineData(true,  false, false, true,  true, false, false, false, false)]
    [InlineData(true,  false, true,  false, true, false, false, false, false)]
    [InlineData(true,  false, true,  true,  true, false, false, false, false)]
    [InlineData(true,  true,  false, false, true, false, false, false, true)]
    [InlineData(true,  true,  false, true,  true, false, false, false, true)]
    [InlineData(true,  true,  true,  false, true, false, false, false, true)]
    [InlineData(true,  true,  true,  true,  true, false, false, false, true)]
    [InlineData(false, false, false, false, false, true, false, false, false)]
    [InlineData(false, false, false, true,  false, true, true,  false, false)]
    [InlineData(false, false, true,  false, false, true, false, true,  false)]
    [InlineData(false, false, true,  true,  false, true, true,  true,  false)]
    [InlineData(false, true,  false, false, false, true, false, false, true)]
    [InlineData(false, true,  false, true,  false, true, true,  false, true)]
    [InlineData(false, true,  true,  false, false, true, false, true,  true)]
    [InlineData(false, true,  true,  true,  false, true, true,  true,  true)]
    public void EachKindHoldsTheLeversItsTableRowNames(
        bool programFocus, bool networkSwitch, bool dimSwitch, bool inputSwitch,
        bool limits, bool covers, bool blocksInput, bool dims, bool blocksNetwork)
    {
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;
        var plan = FocusSessionPlan.For(kind, networkSwitch, dimSwitch, inputSwitch);

        Assert.Equal(kind, plan.Kind);
        Assert.Equal((limits, covers, blocksInput, dims, blocksNetwork),
                     (plan.LimitsPrograms, plan.CoversScreen, plan.BlocksInput, plan.DimsScreen, plan.BlocksNetwork));
        // Never both: the cover would hide the programs program focus exists to keep usable.
        Assert.False(plan.CoversScreen && plan.LimitsPrograms);
    }

    [Fact]
    public void EachKindNamesTheLeverItAlwaysHoldsAsTheOneThatMustArm()
    {
        Assert.Equal(FocusLever.Programs,
                     FocusSessionPlan.For(FocusSessionKind.ProgramFocus, true, true, true).Required);
        Assert.Equal(FocusLever.Cover,
                     FocusSessionPlan.For(FocusSessionKind.ScreenBreak, false, false, false).Required);
    }

    /// <summary>A missing stored value reads as a screen break, which is only true while it is the
    /// enum's first member.</summary>
    [Fact]
    public void AScreenBreakIsWhatAMissingKindReadsAs() =>
        Assert.Equal(FocusSessionKind.ScreenBreak, default(FocusSessionKind));
}
