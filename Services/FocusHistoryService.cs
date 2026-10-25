using System.Globalization;

namespace FocusDesk.Services;

/// <summary>
/// The record of finished focus sessions, one row per session, in the data folder's History
/// subfolder.
/// </summary>
/// <remarks>
/// A session is at most four hours, so the rate is a handful of rows a day at the very most and the
/// file stays small at any plausible use.
/// </remarks>
internal static class FocusHistoryService
{
    /// <summary>A year of sessions is long enough to look back over a project, and the row cap holds
    /// the file to a few tens of kilobytes whatever the rate.</summary>
    internal const int RetentionDays = 365;

    internal const int MaxRows = 500;

    internal const string FileName = "focus-history.csv";

    internal static readonly string HeaderComment =
        "# FocusDesk focus-session history — one row per finished session, kept for " +
        RetentionDays.ToString(CultureInfo.InvariantCulture) + " days. " +
        "started/due/ended = ISO 8601 with local UTC offset; " +
        "due = the end time the session was armed for; " +
        "levers = network, screen, cover and input, joined with +, or none; " +
        "outcome = ran-to-time, ended-early or found-stale; " +
        "programs = limited or not-limited, absent on rows written before the column existed; " +
        "kind = program-focus or screen-break, absent on rows written before the column existed.";
    internal const string HeaderColumns = "started,due,ended,levers,outcome,programs,kind";
    internal static readonly string Header = HeaderComment + "\n" + HeaderColumns;

    private static readonly CsvSampleStore _store = new(FileName, Header);

    private static readonly Lock _lock = new();

    private static DateTime? _prunedOnLocalDate;

    public static string FilePath => _store.FilePath;

    /// <summary>Test-only seam: an isolated file, and a reset of the once-a-day prune that is
    /// otherwise static and would leak from one test to the next.</summary>
    internal static void UseTestPath(string path)
    {
        lock (_lock)
        {
            _store.UseTestPath(path);
            _prunedOnLocalDate = null;
        }
    }

    /// <summary>Appends one finished session. Safe to call from the engine's own lock; never
    /// throws.</summary>
    public static void Record(FocusHistoryEntry entry)
    {
        lock (_lock)
        {
            try
            {
                _store.AppendLine(Format(entry));
                Prune();
            }
            catch (Exception ex)
            {
                // A lost row costs one session's record, which is never worth taking the
                // application down for.
                AppLog.Error("FocusHistoryService.Record", ex);
            }
        }
    }

    /// <summary>The most recent sessions, newest first. Nothing when the file does not exist
    /// yet.</summary>
    public static IReadOnlyList<FocusHistoryEntry> Recent(int count)
    {
        if (count <= 0) return [];

        lock (_lock)
        {
            var found = new List<FocusHistoryEntry>();
            try
            {
                foreach (string line in _store.ReadAllLines())
                    if (TryParse(line, out var entry)) found.Add(entry);
            }
            catch (Exception ex)
            {
                AppLog.Error("FocusHistoryService.Recent", ex);
            }

            found.Reverse();
            return found.Count > count ? found.GetRange(0, count) : found;
        }
    }

    /// <summary>Drops rows past retention, at most once per local day. Called under the
    /// lock.</summary>
    private static void Prune()
    {
        var today = DateTime.Now.Date;
        if (_prunedOnLocalDate == today) return;
        _prunedOnLocalDate = today;

        var cutoff = DateTimeOffset.Now - TimeSpan.FromDays(RetentionDays);
        _store.Prune(
            line => !TryParse(line, out var entry) ? CsvRowVerdict.NotARow
                  : entry.EndedAt < cutoff          ? CsvRowVerdict.Expired
                                                    : CsvRowVerdict.Keep,
            MaxRows);
    }

    /// <summary>The word each outcome is stored as.</summary>
    internal static string Word(FocusSessionOutcome outcome) => outcome switch
    {
        FocusSessionOutcome.RanToTime  => "ran-to-time",
        FocusSessionOutcome.EndedEarly => "ended-early",
        _                              => "found-stale",
    };

    /// <summary>The outcome a stored word names, or null for a word from no version of this
    /// file.</summary>
    internal static FocusSessionOutcome? Outcome(string word) => word switch
    {
        "ran-to-time"  => FocusSessionOutcome.RanToTime,
        "ended-early"  => FocusSessionOutcome.EndedEarly,
        "found-stale"  => FocusSessionOutcome.FoundStale,
        _              => null,
    };

    /// <summary>The levers a session owned, joined with a plus because a comma separates the
    /// columns.</summary>
    internal static string Levers(bool network, bool screen, bool cover, bool input)
    {
        var levers = new List<string>(4);
        if (network) levers.Add("network");
        if (screen) levers.Add("screen");
        if (cover)  levers.Add("cover");
        if (input)  levers.Add("input");
        return levers.Count > 0 ? string.Join('+', levers) : "none";
    }

    /// <summary>The words the final column holds. Stored, so they never change.</summary>
    internal const string ProgramsLimited = "limited";
    internal const string ProgramsNotLimited = "not-limited";

    internal static string Format(FocusHistoryEntry entry)
    {
        string row = string.Join(',',
            Stamp(entry.StartedAt), Stamp(entry.DueAt), Stamp(entry.EndedAt),
            Levers(entry.BlockedNetwork, entry.DimmedScreen, entry.CoveredScreen, entry.BlockedInput),
            Word(entry.Outcome),
            entry.LimitedPrograms ? ProgramsLimited : ProgramsNotLimited);
        // An entry with no kind is one read from an earlier row, which carried no such column.
        return entry.Kind is { } kind ? row + "," + FocusSessionKinds.Word(kind) : row;
    }

    private static string Stamp(DateTimeOffset at) =>
        at.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    internal static bool TryParse(string line, out FocusHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(line);

        entry = default;
        string[] parts = line.Split(',');
        if (parts.Length < 5) return false;

        var ci = CultureInfo.InvariantCulture;
        if (!DateTimeOffset.TryParse(parts[0], ci, DateTimeStyles.RoundtripKind, out var started)) return false;
        if (!DateTimeOffset.TryParse(parts[1], ci, DateTimeStyles.RoundtripKind, out var due)) return false;
        if (!DateTimeOffset.TryParse(parts[2], ci, DateTimeStyles.RoundtripKind, out var ended)) return false;
        if (Outcome(parts[4]) is not { } outcome) return false;

        string[] levers = parts[3].Split('+');
        // A row written before the programs column existed reads as a session that limited nothing,
        // and one written before the kind column existed carries no kind.
        entry = new FocusHistoryEntry(
            started, due, ended,
            levers.Contains("screen"), levers.Contains("cover"), outcome, levers.Contains("input"),
            levers.Contains("network"), parts.Length > 5 && parts[5] == ProgramsLimited,
            parts.Length > 6 ? FocusSessionKinds.FromWord(parts[6]) : null);
        return true;
    }

    /// <summary>One finished session as the status window's two lines: when it started, how long it
    /// was and how it ended; then its kind and the restrictions that held.</summary>
    public static (string Summary, string Restrictions) Describe(FocusHistoryEntry entry) =>
        Describe(entry, AppText.Get, CultureInfo.CurrentCulture);

    /// <summary>The same two lines, composed through <paramref name="text"/> so a test can use each
    /// shipped resource file.</summary>
    internal static (string Summary, string Restrictions) Describe(
        FocusHistoryEntry entry, Func<string, string> text, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(text);

        // The armed length, so a session found finished later never counts the hours the app was closed.
        var length = entry.Outcome == FocusSessionOutcome.EndedEarly
            ? entry.EndedAt - entry.StartedAt
            : entry.DueAt - entry.StartedAt;
        string summary = string.Format(culture, text("HistoryRowSummary"),
            entry.StartedAt.ToLocalTime().ToString("d MMM HH:mm", culture),
            Math.Max(0, (int)length.TotalMinutes),
            text(entry.Outcome switch
            {
                FocusSessionOutcome.RanToTime  => "HistoryOutcomeCompleted",
                FocusSessionOutcome.EndedEarly => "HistoryOutcomeEndedEarly",
                _                              => "HistoryOutcomeEndedWhileClosed",
            }));

        var held = new List<string>(4);
        if (entry.BlockedNetwork) held.Add(text("HistoryRestrictionNetwork"));
        if (entry.DimmedScreen)   held.Add(text("HistoryRestrictionDimmed"));
        if (entry.CoveredScreen)  held.Add(text("HistoryRestrictionCovered"));
        if (entry.BlockedInput)   held.Add(text("HistoryRestrictionInput"));
        string list = string.Join(", ", held);

        // A row written before the kind column existed says only what held.
        string restrictions = entry.Kind switch
        {
            null when held.Count == 0 => text("HistoryNoExtraRestrictions"),
            null                      => list,
            { } kind => held.Count == 0
                ? KindName(kind, text)
                : string.Format(culture, text("HistoryRowKindAndRestrictions"), KindName(kind, text), list),
        };
        return (summary, restrictions);
    }

    private static string KindName(FocusSessionKind kind, Func<string, string> text) =>
        text(kind == FocusSessionKind.ProgramFocus ? "FocusKindProgramFocus" : "FocusKindScreenBreak");
}
