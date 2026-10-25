using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>When marked programs are opened; written out row by row so a changed rule is caught.</summary>
public class ProgramStartRulesTests
{
    private const string Mode1 = nameof(ProgramFocusMode.AllowListed);
    private const string Mode2 = nameof(ProgramFocusMode.OpenMarkedAllowListed);
    private const string Mode3 = nameof(ProgramFocusMode.OpenMarkedAllowAll);
    private const string Start = nameof(ProgramStartTrigger.Start);
    private const string Grace = nameof(ProgramStartTrigger.GraceChange);
    private const string Resume = nameof(ProgramStartTrigger.Resume);

    // program focus (else screen break), mode, trigger → opens
    [Theory]
    [InlineData(true,  Mode2, Start,  true)]
    [InlineData(true,  Mode3, Start,  true)]
    [InlineData(true,  Mode2, Grace,  true)]
    [InlineData(true,  Mode3, Grace,  true)]
    [InlineData(true,  Mode2, Resume, false)]
    [InlineData(true,  Mode3, Resume, false)]
    [InlineData(true,  Mode1, Start,  false)]
    [InlineData(true,  Mode1, Grace,  false)]
    [InlineData(true,  Mode1, Resume, false)]
    [InlineData(false, Mode1, Start,  false)]
    [InlineData(false, Mode2, Start,  false)]
    [InlineData(false, Mode3, Start,  false)]
    [InlineData(false, Mode3, Grace,  false)]
    public void ProgramsOpenOnlyForAFreshProgramFocusStartOrGraceChangeInAnOpeningMode(
        bool programFocus, string mode, string how, bool opens)
    {
        var kind = programFocus ? FocusSessionKind.ProgramFocus : FocusSessionKind.ScreenBreak;
        Assert.Equal(opens, ProgramStartRules.Opens(kind, Enum.Parse<ProgramFocusMode>(mode),
                                                    Enum.Parse<ProgramStartTrigger>(how)));
    }
}
