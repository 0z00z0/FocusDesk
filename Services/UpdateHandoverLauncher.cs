using ZeroZero.Update;

namespace FocusDesk.Services;

/// <summary>
/// Starts Setup the way the shared component does, and records the handover on the way past. The
/// record goes down as Setup starts and not before: Setup replaces the files this process holds, so
/// the process is gone before an outcome exists, while an offer that is declined must leave nothing
/// behind for the next start to report.
/// </summary>
internal sealed class UpdateHandoverLauncher(IInstallerLauncher inner, Action<string> record)
    : IInstallerLauncher
{
    /// <summary>The release an install is running for, set before the flow is started.</summary>
    internal string? TargetVersion { get; set; }

    public void Start(string path, string arguments)
    {
        if (TargetVersion is { Length: > 0 } version) record(version);
        else AppLog.Info("Update: starting Setup with no target version recorded.");

        inner.Start(path, arguments);
    }
}
