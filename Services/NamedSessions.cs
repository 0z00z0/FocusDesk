using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>One named session for Program focus: its mode and its own program list.</summary>
internal sealed class NamedSession
{
    /// <summary>Generated once, never edited or reused; every lookup is by this, never the name.</summary>
    [JsonPropertyOrder(1)] public string Key { get; set; } = "";

    [JsonPropertyOrder(2)] public string Name { get; set; } = "";

    /// <summary>The mode a start uses unless that start chooses another.</summary>
    [JsonPropertyOrder(3)] public ProgramFocusMode Mode { get; set; }

    [JsonPropertyOrder(4)] public List<FocusProgramEntry> Programs { get; set; } = [];

    public static NamedSession Create(string name, List<FocusProgramEntry>? programs = null) => new()
    {
        Key = NamedSessions.NewKey(),
        Name = name,
        Programs = programs ?? [],
    };
}

/// <summary>The rules of the named-session list. The settings reader keeps at least one session in
/// it, so every rule here may take the first entry.</summary>
internal static class NamedSessions
{
    /// <summary>What a choice of the session in use came to.</summary>
    public enum Choice { Chosen, SessionRunning, NotAnOption }

    public const int Maximum = 5;

    public const int NameLength = 20;

    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>The first session's name in the interface language; data from then on.</summary>
    public static string DefaultName()
    {
        const string key = "NamedSessionDefaultName";
        string text = AppText.Get(key);
        return text == key ? "Default" : text;
    }

    public static NamedSession? Find(AppSettings s, string? key)
    {
        ArgumentNullException.ThrowIfNull(s);
        return key is null ? null : s.NamedSessions.FirstOrDefault(n => n.Key == key);
    }

    /// <summary>The session an MQTT start runs on; the first where the stored key names none.</summary>
    public static NamedSession InUse(AppSettings s) =>
        Find(s, s.SelectedNamedSession) ?? s.NamedSessions[0];

    /// <summary>The session a start runs on: the one in use where no key was passed, and null where a
    /// passed key names nothing, so a session deleted or renamed away is refused rather than swapped.</summary>
    public static NamedSession? ForStart(AppSettings s, string? key)
    {
        ArgumentNullException.ThrowIfNull(s);
        return key is null ? InUse(s) : Find(s, key);
    }

    /// <summary>The session the levers read: the one being armed, else the running one, else the one
    /// in use, so changing the session in use never moves a running session's list.</summary>
    public static NamedSession ForLevers(AppSettings s, string? arming)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (Find(s, arming) is { } armed) return armed;
        if (s.FocusSessionEndsAt is null) return InUse(s);
        if (Find(s, s.FocusSessionNamedSession) is { } running) return running;

        // A screen break names no session, so it reads the one in use without a log line.
        if (s.FocusSessionLimitedPrograms)
            AppLog.Info("Focus: the running session's named session is no longer in the list, so the "
                      + "session in use is read instead.");
        return InUse(s);
    }

    /// <summary>One option per session in list order; a taken option gets " (2)", " (3)" and onwards
    /// until it is free, so every option names exactly one session.</summary>
    public static IReadOnlyList<string> Options(IEnumerable<NamedSession> sessions)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        var options = new List<string>();
        foreach (var session in sessions)
        {
            string option = session.Name;
            for (int n = 2; !taken.Add(option); n++) option = $"{session.Name} ({n})";
            options.Add(option);
        }
        return options;
    }

    /// <summary>The option naming the session with <paramref name="key"/>, or null where none does.</summary>
    public static string? OptionOf(IReadOnlyList<NamedSession> sessions, string? key)
    {
        int index = sessions.ToList().FindIndex(n => n.Key == key);
        return index < 0 ? null : Options(sessions)[index];
    }

    /// <summary>Marks the session an option names as in use, refused while a session runs.</summary>
    public static Choice Choose(AppSettings s, string option, bool sessionRunning)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (sessionRunning) return Choice.SessionRunning;
        int index = Options(s.NamedSessions).ToList().IndexOf(option);
        if (index < 0) return Choice.NotAnOption;
        s.SelectedNamedSession = s.NamedSessions[index].Key;
        return Choice.Chosen;
    }

    /// <summary>Adds a session named <paramref name="baseName"/>, numbered from 2 where the name is
    /// taken; null at <see cref="Maximum"/>.</summary>
    public static NamedSession? Add(AppSettings s, string baseName)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.NamedSessions.Count >= Maximum) return null;

        string name = baseName;
        for (int n = 2; s.NamedSessions.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)); n++)
            name = CleanName($"{baseName} {n}", baseName);
        var session = NamedSession.Create(name);
        s.NamedSessions.Add(session);
        return session;
    }

    /// <summary>Removes a session, never the last one; deleting the one in use moves "In use" to the
    /// first remaining.</summary>
    public static bool Delete(AppSettings s, string key)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.NamedSessions.Count <= 1 || Find(s, key) is not { } session) return false;

        bool wasInUse = InUse(s) == session;
        s.NamedSessions.Remove(session);
        if (wasInUse) s.SelectedNamedSession = s.NamedSessions[0].Key;
        return true;
    }

    /// <summary>A name as stored: trimmed, capped, and the previous one where the edit is blank.</summary>
    public static string CleanName(string? typed, string previous)
    {
        string name = (typed ?? "").Trim();
        if (name.Length == 0) return previous;
        return name.Length > NameLength ? name[..NameLength].TrimEnd() : name;
    }
}
