using FocusDesk.Helpers;

namespace FocusDesk.Services;

/// <summary>
/// Turns physical mouse and keyboard input off for the length of a focus session, built so that
/// every way of losing control of it ends with input back.
/// </summary>
/// <remarks>
/// <para>Windows lets only the thread that blocked input release it, and unblocks input when that
/// thread exits, so the block lives on a thread of its own. That thread holds a deadline rather than
/// a flag: it releases and ends unless something keeps pushing the deadline forward, so an
/// application that hangs lifts the block within the renewal window instead of leaving a machine
/// nobody can type on.</para>
/// <para>Each renewal also carries the session's own end time, so the block lifts at that instant
/// whether or not anything is still renewing it.</para>
/// <para>Ctrl+Alt+Delete lifts the block, and the loop puts it back on its next pass. The secure
/// desktop is then the only place a person can act: signing out or restarting from it is the way out
/// of a session whose machine will not answer.</para>
/// </remarks>
/// <param name="setBlocked">The platform call, true to block and false to release. Always called
/// from the holding thread.</param>
/// <param name="log">Where each take and release is written.</param>
internal sealed class InputBlock(
    Func<bool, bool> setBlocked, Action<string> log,
    TimeSpan? renewalWindow = null, TimeSpan? pass = null)
{
    /// <summary>How long the block outlives its last renewal. Three of the session's one-second
    /// ticks, so a tick that is merely late does not drop the block.</summary>
    internal static readonly TimeSpan DefaultRenewalWindow = TimeSpan.FromSeconds(3);

    /// <summary>How often the thread re-asserts the block and re-reads its deadline. Short enough that
    /// Ctrl+Alt+Delete's lift is put back within a second, long enough not to spin.</summary>
    private static readonly TimeSpan DefaultPass = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a take waits for the holding thread to report Windows' answer.</summary>
    private static readonly TimeSpan TakeWait = TimeSpan.FromSeconds(2);

    private readonly TimeSpan _renewalWindow = renewalWindow ?? DefaultRenewalWindow;
    private readonly TimeSpan _pass = pass ?? DefaultPass;
    private readonly Lock _gate = new();

    private Thread? _thread;
    private DateTimeOffset _renewedAt;
    private DateTimeOffset _until;
    private bool _stopping;

    /// <summary>Whether Windows accepted the block. Written by the holding thread before it signals
    /// that it is ready and read by the caller afterwards, so the signal orders the two — taking the
    /// lock before signalling would deadlock against the caller holding it.</summary>
    private volatile bool _took;

    /// <summary>Whether a block is being held right now.</summary>
    internal bool IsBlocking
    {
        get { lock (_gate) return _thread is not null && !_stopping && _took; }
    }

    /// <summary>Blocks input until the renewals stop, or until the session's end time once a renewal
    /// has supplied it. False means Windows refused the block: a lever that did not engage.</summary>
    internal bool Take(ActionCause cause)
    {
        lock (_gate)
        {
            // No end time is known here: the session's tick supplies one within a second, and until
            // it does the renewal deadline alone bounds the block.
            _until = DateTimeOffset.MaxValue;
            _renewedAt = DateTimeOffset.UtcNow;

            if (_thread is not null)
            {
                // A release still winding down is called off; the thread re-reads this on its next
                // pass, and one that has already let go is taken again by the next renewal.
                _stopping = false;
                return _took;
            }

            _stopping = false;
            _took = false;

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => Hold(ready))
            {
                IsBackground = true,
                Name = "FocusDesk input block",
            };
            _thread.Start();
            ready.Wait(TakeWait);

            log(_took
                ? $"Focus: the mouse and keyboard are blocked{cause.Clause}"
                : $"Focus: Windows refused to block the mouse and keyboard{cause.Clause}");
            return _took;
        }
    }

    /// <summary>Pushes the deadline forward. Called from the session's tick: stop calling it and the
    /// block lifts by itself.</summary>
    /// <returns>Whether a block is held to renew.</returns>
    internal bool Renew(DateTimeOffset until)
    {
        lock (_gate)
        {
            if (_thread is null || _stopping || !_took) return false;
            _until = until;
            _renewedAt = DateTimeOffset.UtcNow;
            return true;
        }
    }

    /// <summary>Releases the block. True once nothing is held, including when nothing was.</summary>
    internal bool Release(ActionCause cause)
    {
        Thread? thread;
        lock (_gate)
        {
            if (_thread is null) return true;
            _stopping = true;
            thread = _thread;
        }

        thread.Join(_renewalWindow);
        lock (_gate)
        {
            bool ended = !thread.IsAlive;
            if (ended && _thread == thread) _thread = null;
            log(ended
                ? $"Focus: the mouse and keyboard are back{cause.Clause}"
                : $"Focus: the input block did not release at once, and lapses by itself within "
                + $"{_renewalWindow.TotalSeconds:0} seconds{cause.Clause}");
            return ended;
        }
    }

    /// <summary>The whole of the block's life, on one thread because Windows accepts the release only
    /// from the thread that took it.</summary>
    private void Hold(ManualResetEventSlim ready)
    {
        bool holding = false;
        try
        {
            holding = setBlocked(true);
            _took = holding;
            ready.Set();
            if (!holding) return;

            while (true)
            {
                Thread.Sleep(_pass);

                bool lapse;
                lock (_gate)
                {
                    var now = DateTimeOffset.UtcNow;
                    lapse = _stopping || now - _renewedAt > _renewalWindow || now >= _until;
                }
                if (lapse) return;

                // Ctrl+Alt+Delete lifts the block; this puts it back. Accepted again from the
                // holding thread, so re-asserting costs nothing while it is still in force.
                setBlocked(true);
            }
        }
        catch (Exception ex) { AppLog.Error("InputBlock.Hold", ex); }
        finally
        {
            ready.Set();
            if (holding) setBlocked(false);
            lock (_gate)
            {
                if (_thread == Thread.CurrentThread)
                {
                    _thread = null;
                    _took = false;
                }
            }
        }
    }
}

/// <summary>The input lever: physical mouse and keyboard off for the length of the session. The
/// heaviest lever — while it holds, nothing on the machine answers, this application included.</summary>
/// <remarks>Nothing is displaced and nothing is parked: the block dies with the thread holding it and
/// therefore with the process, so a run that crashed leaves a machine that answers. The block lapses
/// unless the session's tick keeps renewing it, and a block that lapsed while the session still owns
/// it — a sleep longer than the renewal window — is taken again on that tick.</remarks>
internal sealed class FocusInputLever(InputBlock block, Func<string?> refusal) : IFocusLever
{
    /// <summary>Why the block would be refused: without administrator rights Windows refuses every
    /// call, so a session would arm a lever that does nothing.</summary>
    internal static string? ElevationRefusal() =>
        Elevation.IsElevated
            ? null
            : "blocking the mouse and keyboard needs administrator rights, which this run does not have";

    public string? Refusal() => refusal();

    public bool Engage(ActionCause cause) => block.Take(cause);

    /// <summary>Blocks again. Nothing survives the process ending, so resuming means taking the
    /// block afresh rather than finding it still held.</summary>
    public bool Resume(ActionCause cause) => block.Take(cause);

    public bool Hold(DateTimeOffset until, ActionCause cause)
    {
        if (block.Renew(until)) return true;
        return block.Take(cause) && block.Renew(until);
    }

    public bool Lift(ActionCause cause) => block.Release(cause);
}
