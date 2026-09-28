using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>The two kinds of focus session. A kind is a fixed choice over the levers the session
/// holds; <see cref="FocusSessionPlan"/> is where that choice is made.</summary>
/// <remarks>Stored by member name in the settings document, so a renamed member resets the choice on
/// every installation. <see cref="ScreenBreak"/> is first, so a missing value reads as a screen
/// break.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum FocusSessionKind
{
    /// <summary>Every display is covered; the mouse and keyboard, the screen's brightness and the
    /// network can be held as well.</summary>
    ScreenBreak,

    /// <summary>Only the programs ticked "Can run" can be worked in; the network can be held as
    /// well.</summary>
    ProgramFocus,
}

/// <summary>The words a kind is written as outside the application: the history file's column and
/// Home Assistant's select. Both are stored or published vocabularies, never translated.</summary>
internal static class FocusSessionKinds
{
    /// <summary>The history file's words. Written into a file a person reads, so they never
    /// change.</summary>
    public const string ProgramFocusWord = "program-focus";
    public const string ScreenBreakWord  = "screen-break";

    /// <summary>Home Assistant's option words. A receiver's automation compares against these
    /// literals, so they never change and never follow the interface language.</summary>
    public const string ProgramFocusOption = "Program focus";
    public const string ScreenBreakOption  = "Screen break";

    /// <summary>Every option the select offers, in the order the kinds are declared.</summary>
    public static IReadOnlyList<string> Options { get; } =
        [.. Enum.GetValues<FocusSessionKind>().Select(Option)];

    public static string Word(FocusSessionKind kind) =>
        kind == FocusSessionKind.ProgramFocus ? ProgramFocusWord : ScreenBreakWord;

    /// <summary>The kind a stored word names, or null for a word from no version of the file.</summary>
    public static FocusSessionKind? FromWord(string word) => word switch
    {
        ProgramFocusWord => FocusSessionKind.ProgramFocus,
        ScreenBreakWord  => FocusSessionKind.ScreenBreak,
        _                => null,
    };

    public static string Option(FocusSessionKind kind) =>
        kind == FocusSessionKind.ProgramFocus ? ProgramFocusOption : ScreenBreakOption;

    /// <summary>The kind an option names, or null for anything else.</summary>
    public static FocusSessionKind? FromOption(string option) => option switch
    {
        ProgramFocusOption => FocusSessionKind.ProgramFocus,
        ScreenBreakOption  => FocusSessionKind.ScreenBreak,
        _                  => null,
    };
}
