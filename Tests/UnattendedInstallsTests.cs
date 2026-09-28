using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FocusDesk.Services;
using Xunit;
using ZeroZero.Primitives;
using ZeroZero.Update;
using ZeroZero.Update.Win32;

namespace FocusDesk.Tests;

/// <summary>
/// Installing with nobody asked, driven through the shared component's real policy with exactly the
/// options FocusDesk hands it: a running focus session refuses the moment even where the component
/// itself would start the installer, and the switch is off unless somebody turns it on. Nothing here
/// downloads, starts a process or reaches a screen.
/// </summary>
public class UnattendedInstallsTests
{
    private static readonly ReleaseInfo Release = new(
        "v9.9.9", new Version(9, 9, 9, 0), "9.9.9", "FocusDesk v9.9.9", "notes",
        null, null, []);

    private static readonly FocusSnapshot Running = new(
        FocusSessionStage.Active, DateTimeOffset.Now, DateTimeOffset.Now.AddHours(1),
        DimsScreen: true, CoversScreen: true, BlocksInput: true);

    /// <summary>The guard. The screen is locked, so the component's own rule passes; only the
    /// session stands between the machine and an installer that closes FocusDesk. Every stage but
    /// off refuses, a cancel in progress included. The session then ends and the same policy starts
    /// the installer, so the refusal is the rule and not a fixture that could never reach a launch.</summary>
    [Fact]
    public async Task ARunningSessionRefusesTheMoment_AndTheEndOfTheSessionLetsItThrough()
    {
        var service = new FakeUpdateService();
        var session = Running;
        var installing = new List<string>();
        int shutdowns = 0;

        using var policy = new UnattendedUpdatePolicy(service,
            UnattendedInstalls.Options(enabled: true, () => session, installing.Add, () => shutdowns++,
                                       NullLogSink.Instance),
            new LockedScreen());

        foreach (var stage in new[] { FocusSessionStage.Active, FocusSessionStage.Ending, FocusSessionStage.Confirm })
        {
            session = Running with { Stage = stage };
            UnattendedTick refused = await policy.TickAsync();

            Assert.Equal(UnattendedOutcome.Refused, refused.Outcome);
            Assert.Equal(UnattendedInstalls.SessionRunningReason, refused.Reason);
        }

        Assert.Equal(0, service.Launches);
        Assert.Equal(0, shutdowns);
        Assert.Empty(installing);

        session = FocusSnapshot.None;
        UnattendedTick started = await policy.TickAsync();

        Assert.Equal(UnattendedOutcome.InstallerStarted, started.Outcome);
        Assert.Equal(1, service.Launches);
        Assert.Equal(1, shutdowns);
        // The handover the next start reports on names the release that was started.
        Assert.Equal(["9.9.9"], installing);
    }

    /// <summary>A value that must never move: an installation that has opened nothing, and a document
    /// written before the row existed, both read the switch as off.</summary>
    [Fact]
    public void TheSwitchIsOffUnlessTurnedOn()
    {
        Assert.False(new AppSettings().InstallUpdatesUnattended);
        Assert.False(new SettingsFile().ToSettings().InstallUpdatesUnattended);
    }

    private sealed class LockedScreen : IMachineIdle
    {
        public MachineIdleReading Read() => new(TimeSpan.Zero, ScreenLocked: true);
    }

    /// <summary>Answers a release, a verified download and a started installer, and counts the
    /// launches. No network, no file, no process.</summary>
    private sealed class FakeUpdateService : IUpdateService
    {
        public Version RunningVersion { get; } = new(1, 0, 0, 0);

        public int Launches { get; private set; }

        public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new UpdateCheckResult(UpdateCheckOutcome.UpdateAvailable, RunningVersion, Release));

        public Task<PreparedUpdate> PrepareAsync(ReleaseInfo release, IProgress<DownloadProgress>? progress = null,
                                                 CancellationToken cancellationToken = default) =>
            Task.FromResult(new PreparedUpdate(PrepareOutcome.Ready, release, "FocusDesk-Setup-9.9.9.exe",
                                               @"C:\nowhere\FocusDesk-Setup-9.9.9.exe", "00",
                                               new VerificationResult(VerificationVerdict.Verified, "verified"),
                                               "verified"));

        public LaunchResult Launch(PreparedUpdate update)
        {
            Launches++;
            return new LaunchResult(true, "started");
        }

        public int SweepStaleDownloads(TimeSpan olderThan) => 0;
    }
}
