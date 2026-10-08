namespace FocusDesk.Services;

/// <summary>
/// The goal typed for one session: a single line that stands in for the cover's headline and the
/// focus point's quiet line while it runs.
/// </summary>
/// <remarks>Private to the screen. The log records only that a goal was set, and no published surface
/// — the tray, Home Assistant, the history file — carries it.</remarks>
internal static class FocusSessionGoal
{
    internal const int MaxLength = 80;

    /// <summary>The goal as it is kept: control characters removed, trimmed, and cut to
    /// <see cref="MaxLength"/>. Empty for nothing typed.</summary>
    internal static string Clean(string? typed)
    {
        if (string.IsNullOrEmpty(typed)) return "";
        string line = new string([.. typed.Where(c => !char.IsControl(c))]).Trim();
        if (line.Length <= MaxLength) return line;
        // Never half of a character drawn from two code units.
        int cut = char.IsHighSurrogate(line[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return line[..cut].TrimEnd();
    }

    /// <summary>The goal where one was set, and <paramref name="standard"/> where none was.</summary>
    internal static string Or(string? goal, string standard) =>
        Clean(goal) is { Length: > 0 } set ? set : standard;
}
