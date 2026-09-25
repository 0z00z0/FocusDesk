using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>What happened to a request to change the program list. <see cref="Added"/>,
/// <see cref="Removed"/> and <see cref="Changed"/> are the only outcomes that changed anything.</summary>
internal enum FocusAllowVerdict
{
    Added,
    Removed,

    /// <summary>One of a row's two checkboxes moved.</summary>
    Changed,

    /// <summary>A session is running, so the list may not move. Changing it mid-session would either
    /// leave a program with a rule the session never wrote, or take away one it did.</summary>
    SessionRunning,

    /// <summary>Not a program this can name: no program file, package family or web-app
    /// identity.</summary>
    NotAProgram,

    /// <summary>The list already holds <see cref="FocusAllowedPrograms.Maximum"/> programs.</summary>
    ListFull,

    /// <summary>Already on the list.</summary>
    AlreadyAllowed,

    /// <summary>Not on the list, so there was nothing to take off or change.</summary>
    NotAllowed,

    /// <summary>The checkbox does nothing for this kind of program: a firewall rule by path cannot
    /// name a Store package, and a web app's network belongs to its browser's row.</summary>
    NotOffered,
}

/// <summary>How a program is identified. The member names are stored, so they never change.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum FocusProgramKind
{
    /// <summary>A program file, by its full path.</summary>
    ProgramFile,

    /// <summary>A Store app, by its package family name.</summary>
    StorePackage,

    /// <summary>A web app, by its own application identity.</summary>
    WebApp,
}

/// <summary>What happens to a program's window while it is not allowed to run. The member names are
/// stored, so they never change.</summary>
/// <remarks>Only <see cref="Minimise"/> is carried out in this version. The other two are stored so a
/// later version can act on them, and are read here as <see cref="Minimise"/>.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum FocusProgramAction
{
    Minimise,
    AskToClose,
    ForceClose,
}

/// <summary>One program on the list: what it is, the two choices made for it, and what happens to it
/// while it may not run. No name is stored: the name shown is read from the Start entry each
/// time.</summary>
internal sealed record FocusProgramEntry
{
    /// <summary>How <see cref="Id"/> is to be read.</summary>
    [JsonPropertyOrder(1)] public FocusProgramKind Kind { get; init; }

    /// <summary>The full path, the package family name, or the web app's application identity.</summary>
    [JsonPropertyOrder(2)] public string Id { get; init; } = "";

    /// <summary>The browser file a web app's shortcut starts. Shown beneath the row, never matched
    /// on.</summary>
    [JsonPropertyOrder(3)] public string? BrowserPath { get; init; }

    /// <summary>The Start entry the program was picked from — a shortcut's path or an Apps-folder
    /// parsing name — for its icon and name.</summary>
    [JsonPropertyOrder(4)] public string? StartEntry { get; init; }

    [JsonPropertyOrder(5)] public bool CanRun { get; init; }
    [JsonPropertyOrder(6)] public bool CanUseNetwork { get; init; }
    [JsonPropertyOrder(7)] public FocusProgramAction WhenNotAllowed { get; init; } = FocusProgramAction.Minimise;
}

/// <summary>
/// The one program list: which programs a session lets run, and which keep the network while a
/// session blocks it. Two checkboxes per row, read as two lists.
/// </summary>
/// <remarks>
/// A program is named by a stable identifier — a program file's path, which is also what a firewall
/// rule keys on, a Store package family, or a web app's application identity. A display name or a
/// window title is neither unique nor stable and is never stored or looked up.
/// </remarks>
internal static class FocusAllowedPrograms
{
    /// <summary>Enough that a session can keep a working set, small enough that the rules written at
    /// the start of one stay countable.</summary>
    public const int Maximum = 20;

    /// <summary>The stored form of a chosen path: trimmed, with the separators the platform uses, so
    /// two spellings of one file do not both reach the list.</summary>
    /// <remarks>A path that is not already rooted comes back empty rather than resolved. Resolving
    /// one against the working directory would turn a bare file name into a rooted path to a file
    /// that is very probably not the program meant.</remarks>
    public static string Normalise(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        string trimmed = path.Trim().Trim('"');
        if (!Path.IsPathRooted(trimmed)) return "";

        try { return Path.GetFullPath(trimmed); }
        catch { return ""; }
    }

    /// <summary>Whether a path can be written into a firewall rule at all.</summary>
    public static bool IsProgram(string path) =>
        path.Length > 0
        && Path.IsPathRooted(path)
        && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether two stored identifiers name the same program. Windows paths, package family
    /// names and application identities all compare without case, and always whole.</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>The stored form of an identifier of the given kind, or empty where it names
    /// nothing.</summary>
    public static string NormaliseId(FocusProgramKind kind, string? id)
    {
        if (kind == FocusProgramKind.ProgramFile)
        {
            string path = Normalise(id);
            return IsProgram(path) ? path : "";
        }

        // A package family or an application identity is one token: a separator means a path has
        // been handed in where an identity was expected.
        string token = (id ?? "").Trim();
        return token.Length > 0 && token.IndexOfAny(['\\', '/', ' ']) < 0 ? token : "";
    }

    /// <summary>Whether an entry names this program.</summary>
    public static bool Names(FocusProgramEntry entry, FocusProgramKind kind, string id) =>
        entry.Kind == kind && Same(entry.Id, id);

    /// <summary>Whether "can use the network" means anything for this kind: a firewall rule by path
    /// names a program file and nothing else.</summary>
    public static bool NetworkApplies(FocusProgramKind kind) => kind == FocusProgramKind.ProgramFile;

    /// <summary>What the row shows when no Start entry supplies a name: the file's own name, or the
    /// identifier itself.</summary>
    public static string DisplayName(FocusProgramEntry entry) =>
        entry.Kind == FocusProgramKind.ProgramFile
        && Path.GetFileNameWithoutExtension(entry.Id) is { Length: > 0 } name
            ? name
            : entry.Id;

    /// <summary>What the row shows beneath the name: the program file, the package, or the browser a
    /// web app runs in.</summary>
    public static string Describe(FocusProgramEntry entry) =>
        entry.Kind == FocusProgramKind.WebApp && entry.BrowserPath is { Length: > 0 } browser
            ? browser
            : entry.Id;

    /// <summary>Adds one program, or says why it was not added. <paramref name="list"/> is changed
    /// only on <see cref="FocusAllowVerdict.Added"/>.</summary>
    /// <remarks>"Can use the network" is kept only where it applies, so a Store app or a web app
    /// never carries a network choice nothing would honour.</remarks>
    public static FocusAllowVerdict Add(IList<FocusProgramEntry> list, FocusProgramEntry? candidate,
                                        bool sessionRunning)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (sessionRunning) return FocusAllowVerdict.SessionRunning;
        if (candidate is null) return FocusAllowVerdict.NotAProgram;

        string id = NormaliseId(candidate.Kind, candidate.Id);
        if (id.Length == 0) return FocusAllowVerdict.NotAProgram;
        if (list.Any(e => Names(e, candidate.Kind, id))) return FocusAllowVerdict.AlreadyAllowed;
        if (list.Count >= Maximum) return FocusAllowVerdict.ListFull;

        list.Add(candidate with
        {
            Id = id,
            CanUseNetwork = candidate.CanUseNetwork && NetworkApplies(candidate.Kind),
        });
        return FocusAllowVerdict.Added;
    }

    /// <summary>Takes one program off, or says why it was not taken off.</summary>
    public static FocusAllowVerdict Remove(IList<FocusProgramEntry> list, FocusProgramKind kind,
                                           string? id, bool sessionRunning)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (sessionRunning) return FocusAllowVerdict.SessionRunning;

        int at = IndexOf(list, kind, id);
        if (at < 0) return FocusAllowVerdict.NotAllowed;

        list.RemoveAt(at);
        return FocusAllowVerdict.Removed;
    }

    /// <summary>Ticks or clears "can run" on one row.</summary>
    public static FocusAllowVerdict SetCanRun(IList<FocusProgramEntry> list, FocusProgramKind kind,
                                              string? id, bool canRun, bool sessionRunning) =>
        Change(list, kind, id, sessionRunning, e => e with { CanRun = canRun }, offered: true);

    /// <summary>Ticks or clears "can use the network" on one row. Refused where the checkbox does
    /// nothing for that kind.</summary>
    public static FocusAllowVerdict SetCanUseNetwork(IList<FocusProgramEntry> list, FocusProgramKind kind,
                                                     string? id, bool canUseNetwork, bool sessionRunning) =>
        Change(list, kind, id, sessionRunning, e => e with { CanUseNetwork = canUseNetwork },
               offered: NetworkApplies(kind));

    private static FocusAllowVerdict Change(IList<FocusProgramEntry> list, FocusProgramKind kind,
                                            string? id, bool sessionRunning,
                                            Func<FocusProgramEntry, FocusProgramEntry> change,
                                            bool offered)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (sessionRunning) return FocusAllowVerdict.SessionRunning;
        if (!offered) return FocusAllowVerdict.NotOffered;

        int at = IndexOf(list, kind, id);
        if (at < 0) return FocusAllowVerdict.NotAllowed;

        list[at] = change(list[at]);
        return FocusAllowVerdict.Changed;
    }

    private static int IndexOf(IList<FocusProgramEntry> list, FocusProgramKind kind, string? id)
    {
        string stored = NormaliseId(kind, id);
        for (int i = 0; i < list.Count; i++)
            if (Names(list[i], kind, stored)) return i;
        return -1;
    }

    /// <summary>The paths the network lever writes its rules from: program-file entries with "can use
    /// the network" ticked, in list order, with duplicates dropped.</summary>
    public static IReadOnlyList<string> NetworkPaths(IEnumerable<FocusProgramEntry>? entries) =>
        Usable(entries?.Where(e => e.Kind == FocusProgramKind.ProgramFile && e.CanUseNetwork)
                       .Select(e => e.Id));

    /// <summary>The entries a session lets run: "can run" ticked and naming something, in list order,
    /// with duplicates dropped.</summary>
    public static IReadOnlyList<FocusProgramEntry> RunEntries(IEnumerable<FocusProgramEntry>? entries)
    {
        if (entries is null) return [];

        var usable = new List<FocusProgramEntry>();
        foreach (var entry in entries)
        {
            if (!entry.CanRun) continue;
            string id = NormaliseId(entry.Kind, entry.Id);
            if (id.Length == 0 || usable.Any(e => Names(e, entry.Kind, id))) continue;
            usable.Add(entry with { Id = id });
        }
        return usable;
    }

    /// <summary>The entries an earlier document's path list becomes. Each path was chosen to keep the
    /// network; whether it may run is a choice nobody has made, so it may not.</summary>
    public static List<FocusProgramEntry> Migrate(IEnumerable<string>? paths) =>
        [.. Usable(paths).Select(path => new FocusProgramEntry
        {
            Kind           = FocusProgramKind.ProgramFile,
            Id             = path,
            CanRun         = false,
            CanUseNetwork  = true,
            WhenNotAllowed = FocusProgramAction.Minimise,
        })];

    /// <summary>A path list as a firewall rule set is written from: every entry that still names a
    /// program, in order, with duplicates dropped.</summary>
    public static IReadOnlyList<string> Usable(IEnumerable<string>? stored)
    {
        if (stored is null) return [];

        var usable = new List<string>();
        foreach (string entry in stored)
        {
            string path = Normalise(entry);
            if (IsProgram(path) && !usable.Any(p => Same(p, path))) usable.Add(path);
        }
        return usable;
    }
}
