namespace FocusDesk.Services;

/// <summary>A lever a session can hold, named so a plan can say which one must arm.</summary>
internal enum FocusLever
{
    Screen,
    Cover,
    Input,
    Network,
    Programs,
}

/// <summary>
/// The levers one session arms: its kind turned into lever choices, with the stored switches applied
/// where the kind leaves a lever to them.
/// </summary>
/// <remarks>
/// <para>Pure, and built only through <see cref="For"/>, so no plan can hold the cover and the program
/// limit together, or leave out the lever its kind always holds.</para>
/// <para>Program focus always limits the programs, and never covers, dims, blocks input or blocks the
/// network. A screen break always covers, never limits the programs, and dims, blocks input and blocks
/// the network where their switches say so.</para>
/// </remarks>
internal sealed class FocusSessionPlan
{
    private FocusSessionPlan(FocusSessionKind kind, bool dimsScreen, bool coversScreen,
                             bool blocksInput, bool blocksNetwork, bool limitsPrograms,
                             string? namedSession = null)
    {
        NamedSession = namedSession;
        Kind = kind;
        DimsScreen = dimsScreen;
        CoversScreen = coversScreen;
        BlocksInput = blocksInput;
        BlocksNetwork = blocksNetwork;
        LimitsPrograms = limitsPrograms;
    }

    public FocusSessionKind Kind { get; }

    public bool DimsScreen { get; }

    public bool CoversScreen { get; }

    public bool BlocksInput { get; }

    public bool BlocksNetwork { get; }

    public bool LimitsPrograms { get; }

    /// <summary>The key of the named session a Program focus session runs on; null for a screen break.</summary>
    public string? NamedSession { get; }

    /// <summary>The lever the kind always holds. Where it cannot arm, the session is refused with that
    /// lever's own reason.</summary>
    public FocusLever Required =>
        Kind == FocusSessionKind.ProgramFocus ? FocusLever.Programs : FocusLever.Cover;

    /// <param name="blocksNetwork">Read only by a screen break.</param>
    /// <param name="dimsScreen">Read only by a screen break.</param>
    /// <param name="blocksInput">Read only by a screen break.</param>
    /// <param name="namedSession">The named session's key, read only by Program focus.</param>
    public static FocusSessionPlan For(FocusSessionKind kind, bool blocksNetwork, bool dimsScreen,
                                       bool blocksInput, string? namedSession = null) => kind switch
    {
        FocusSessionKind.ProgramFocus => new(kind, dimsScreen: false, coversScreen: false,
                                             blocksInput: false, blocksNetwork: false, limitsPrograms: true,
                                             namedSession),
        _ => new(FocusSessionKind.ScreenBreak, dimsScreen, coversScreen: true, blocksInput,
                 blocksNetwork, limitsPrograms: false),
    };
}
