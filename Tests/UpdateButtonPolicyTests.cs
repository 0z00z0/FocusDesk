using FocusDesk.Helpers;
using Xunit;
using ZeroZero.Brand.WinUI;
using ZeroZero.Update;
using ZeroZero.Update.Win32;

namespace FocusDesk.Tests;

/// <summary>
/// What the Check for updates button shows, and which outcomes reach the update component's own
/// window instead. An outcome that is neither leaves a click with no answer at all.
/// </summary>
public class UpdateButtonPolicyTests
{
    private static readonly ReleaseInfo Release =
        new("v9.9.9", new Version(9, 9, 9), "9.9.9", "FocusDesk v9.9.9",
            "", new Uri("https://example.invalid"), null, []);

    [Fact]
    public void TheButtonShowsTheOutcomeItCanSayItself()
    {
        Assert.Equal(new UpdateButtonPolicy.Look(BrandBracketButtonState.Success, "Up to date"),
                     UpdateButtonPolicy.After(new UpdateFlowRun(UpdateFlowResult.UpToDate)));

        Assert.Equal(new UpdateButtonPolicy.Look(BrandBracketButtonState.Attention, "Update to v9.9.9"),
                     UpdateButtonPolicy.After(
                         new UpdateFlowRun(UpdateFlowResult.UpdateAvailable, Release: Release)));
    }

    /// <summary>A failure has no state of its own. Leaving the button in attention or success after
    /// one would say the check succeeded.</summary>
    [Fact]
    public void AFailureReturnsTheButtonToRest() =>
        Assert.Equal(UpdateButtonPolicy.Rest,
                     UpdateButtonPolicy.After(new UpdateFlowRun(UpdateFlowResult.CheckFailed)));

    /// <summary>Everything the button cannot say itself opens the window. The two it can say do not,
    /// or a check that found nothing wrong would raise a dialogue over a button already saying so.</summary>
    [Fact]
    public void EveryOutcomeEndsSomewhere()
    {
        Assert.False(UpdateButtonPolicy.ShowsNotice(UpdateFlowResult.UpToDate));
        Assert.False(UpdateButtonPolicy.ShowsNotice(UpdateFlowResult.UpdateAvailable));

        foreach (var result in Enum.GetValues<UpdateFlowResult>())
            if (result is not (UpdateFlowResult.UpToDate or UpdateFlowResult.UpdateAvailable))
                Assert.True(UpdateButtonPolicy.ShowsNotice(result),
                            $"{result} leaves a click with nothing on screen.");
    }
}
