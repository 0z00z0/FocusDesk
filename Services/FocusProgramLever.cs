namespace FocusDesk.Services;

/// <summary>
/// The focus-app lever: only the programs ticked "can run" can be worked in, and every other
/// program's window is minimised as it appears and again whenever it is restored.
/// </summary>
/// <remarks>
/// <para>Fails safe like the network and input levers: refused or failed, the session runs on without
/// it and reports it as not held. A session holding only this lever is allowed.</para>
/// <para>Nothing on the machine is changed, so nothing is put back: lifting stops the watch, and a
/// minimised program stays minimised until somebody restores it.</para>
/// </remarks>
/// <param name="context">The list and the exemptions, read at the moment the session arms or resumes,
/// or null where the list cannot be read. The list cannot move while a session runs, so one reading
/// covers the whole session.</param>
internal sealed class FocusProgramLever(ProgramGate gate, Func<GateContext?> context) : IFocusLever
{
    /// <summary>Why the lever would minimise every program window, which is what happens with no row
    /// that keeps anything usable. Rows for Windows programs do not count: those are usable
    /// anyway.</summary>
    internal static string? NothingKeptUsable(GateContext context) =>
        FocusAllowedPrograms.RunEntries(context.Entries)
                            .Any(e => ProgramCatalogue.OfferFor(e, context.WindowsFolder) != ProgramOffer.NetworkOnly)
            ? null
            : "no program is chosen to stay usable, so every program window would be minimised";

    public string? Refusal() =>
        context() is { } read
            ? NothingKeptUsable(read)
            : "the program list could not be read";

    public bool Engage(ActionCause cause) =>
        context() is { } read && NothingKeptUsable(read) is null && gate.Arm(read);

    /// <summary>Arms again for a session the application was closed or crashed during, with a sweep
    /// at once: nothing of the watch survives the process.</summary>
    public bool Resume(ActionCause cause) => Engage(cause);

    public bool Hold(DateTimeOffset until, ActionCause cause) => gate.Hold();

    public bool Lift(ActionCause cause)
    {
        gate.Lift();
        return true;
    }
}
