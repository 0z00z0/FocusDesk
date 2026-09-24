namespace FocusDesk.Helpers;

/// <summary>
/// The process-wide "only one FocusDesk" lock. Two instances would both try to hold the focus
/// session's levers at once, and the watchdog task's own five-minute probe would otherwise stack a
/// second process on top of one already running. The name never changes, or an old and a new
/// version run side by side during an update.
/// </summary>
internal static class SingleInstanceGuard
{
    private const string MutexName = "Local\\FocusDesk.SingleInstance";

    // Held for the life of the process once acquired; released when the process ends.
    private static Mutex? _mutex;

    /// <summary>One instant, non-blocking attempt at the lock. An abandoned lock counts as taken —
    /// it protects nothing that needs repair.</summary>
    internal static bool TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool acquired);
        if (!acquired)
        {
            try { acquired = mutex.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { acquired = true; }
        }

        if (!acquired)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }
}
