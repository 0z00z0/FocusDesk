using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The input block against a fake platform call: every way of losing control of it ends with the
/// mouse and keyboard back. A block that never releases leaves a machine nobody can type on, and
/// only a restart ends it.
/// </summary>
/// <remarks>Nothing here blocks real input. The windows are shortened so each test takes well under
/// a second of waiting.</remarks>
public class InputBlockTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Pass = TimeSpan.FromMilliseconds(20);

    /// <summary>Records every call the block makes, with the thread it came from.</summary>
    private sealed class FakePlatform(bool accepts = true)
    {
        public ConcurrentQueue<(bool Blocked, int Thread)> Calls { get; } = new();

        public bool Set(bool blocked)
        {
            Calls.Enqueue((blocked, Environment.CurrentManagedThreadId));
            return accepts;
        }

        public bool Released => Calls.Any(c => !c.Blocked);
    }

    private static InputBlock Block(FakePlatform platform) =>
        new(platform.Set, _ => { }, Window, Pass);

    private static bool Within(TimeSpan limit, Func<bool> condition) =>
        SpinWait.SpinUntil(condition, limit);

    [Fact]
    public void ABlockNobodyRenews_ReleasesItselfWithinTheRenewalWindow()
    {
        // The application hanging is the case this is for: no tick, so no renewal.
        var platform = new FakePlatform();
        var block = Block(platform);

        Assert.True(block.Take("a test"));
        Assert.True(Within(Window * 4, () => platform.Released));
        Assert.True(Within(Window, () => !block.IsBlocking));
    }

    [Fact]
    public void TheReleaseComesFromTheThreadThatTookTheBlock()
    {
        // Windows accepts the release from that thread alone; a release from any other thread is
        // refused and leaves the block in force.
        var platform = new FakePlatform();
        var block = Block(platform);

        block.Take("a test");
        Assert.True(block.Release("a test"));

        var calls = platform.Calls.ToArray();
        int taker = calls.First(c => c.Blocked).Thread;
        var release = Assert.Single(calls, c => !c.Blocked);
        Assert.Equal(taker, release.Thread);
        Assert.NotEqual(Environment.CurrentManagedThreadId, taker);
    }

    [Fact]
    public void ABlockRenewedPastTheSessionsEnd_StillLiftsAtThatEnd()
    {
        var platform = new FakePlatform();
        var block = Block(platform);

        block.Take("a test");
        block.Renew(DateTimeOffset.UtcNow.AddMilliseconds(100));

        // Renewals keep arriving, but the end time has passed.
        Assert.True(Within(Window * 4, () =>
        {
            block.Renew(DateTimeOffset.UtcNow.AddMilliseconds(-1));
            return platform.Released;
        }));
    }

    [Fact]
    public void ABlockWindowsRefuses_IsReportedAsNotTakenAndHoldsNothing()
    {
        var platform = new FakePlatform(accepts: false);
        var block = Block(platform);

        Assert.False(block.Take("a test"));
        Assert.True(Within(Window, () => !block.IsBlocking));
        Assert.False(block.Renew(DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.False(platform.Released);
    }
}
