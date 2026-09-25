using ZeroZero.Brand.WinUI;
using ZeroZero.Update.Win32;

namespace FocusDesk.Helpers;

/// <summary>
/// What the Check for updates button shows, and which outcomes are reported in the update
/// component's own window instead. Pure, so what appears where is assertable without a display.
/// </summary>
/// <remarks>Every check runs silently, so the shared flow shows nothing of its own and this is the
/// only rule deciding what a person sees.</remarks>
internal static class UpdateButtonPolicy
{
    internal const string RestLabel     = "Check for updates";
    internal const string CheckingLabel = "Checking…";
    internal const string UpToDateLabel = "Up to date";

    internal static string AvailableLabel(string version) => $"Update to v{version}";

    /// <summary>A state of the button and the label shown with it.</summary>
    internal readonly record struct Look(BrandBracketButtonState State, string Label);

    internal static Look Rest => new(BrandBracketButtonState.Rest, RestLabel);

    internal static Look Checking => new(BrandBracketButtonState.Busy, CheckingLabel);

    /// <summary>The button once a check has ended. A failure has no state of its own and returns the
    /// button to rest, because the window says what happened.</summary>
    internal static Look After(UpdateFlowRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Result switch
        {
            UpdateFlowResult.UpToDate => new(BrandBracketButtonState.Success, UpToDateLabel),
            UpdateFlowResult.UpdateAvailable when run.Release?.VersionText is { Length: > 0 } version
                => new(BrandBracketButtonState.Attention, AvailableLabel(version)),
            _ => Rest,
        };
    }

    /// <summary>Whether the outcome opens the component's own window. The two the button can say
    /// itself do not; everything else would otherwise leave a click with no answer.</summary>
    internal static bool ShowsNotice(UpdateFlowResult result) =>
        result is not (UpdateFlowResult.UpToDate or UpdateFlowResult.UpdateAvailable);
}
