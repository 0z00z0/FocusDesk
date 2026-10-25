namespace FocusDesk.Services;

/// <summary>How a session came to be running when its marked programs could be opened.</summary>
internal enum ProgramStartTrigger
{
    Start,
    GraceChange,
    Resume,
}

/// <summary>What each Program focus mode does, as pure decisions.</summary>
internal static class ProgramStartRules
{
    public static bool OpensMarked(ProgramFocusMode mode) => mode != ProgramFocusMode.AllowListed;

    public static bool LimitsPrograms(ProgramFocusMode mode) => mode != ProgramFocusMode.OpenMarkedAllowAll;

    /// <summary>Never for a screen break or a resumed session: a restart must not open programs twice.</summary>
    public static bool Opens(FocusSessionKind kind, ProgramFocusMode mode, ProgramStartTrigger how) =>
        kind == FocusSessionKind.ProgramFocus && OpensMarked(mode) && how != ProgramStartTrigger.Resume;
}
