using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>One named session for Program focus: its own program list and network switch.</summary>
internal sealed class NamedSession
{
    /// <summary>Generated once, never edited or reused; every lookup is by this, never the name.</summary>
    [JsonPropertyOrder(1)] public string Key { get; set; } = "";

    [JsonPropertyOrder(2)] public string Name { get; set; } = "";

    [JsonPropertyOrder(3)] public bool BlocksNetwork { get; set; }

    [JsonPropertyOrder(4)] public List<FocusProgramEntry> Programs { get; set; } = [];

    public static NamedSession Create(string name, bool blocksNetwork = false,
                                      List<FocusProgramEntry>? programs = null) => new()
    {
        Key = NamedSessions.NewKey(),
        Name = name,
        BlocksNetwork = blocksNetwork,
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

    /// <summary>The session the levers read: the one being armed, else the running one, else the one
    /// in use, so changing the session in use never moves a running session's list.</summary>
    public static NamedSession ForLevers(AppSettings s, string? arming)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (Find(s, arming) is { } armed) return armed;
        if (s.FocusSessionEndsAt is null) return InUse(s);
        if (Find(s, s.FocusSessionNamedSession) is { } running) return running;

        if (s.FocusSessionLimitedPrograms || s.FocusSessionBlockedNetwork)
            AppLog.Info("Focus: the running session's named session is no longer in the list, so the "
                      + "session in use is read instead.");
        return InUse(s);
    }

    /// <summary>One option per session in list order; a repeated name gets " (2)", " (3)" after its
    /// first occurrence, so every option names exactly one session.</summary>
    public static IReadOnlyList<string> Options(IEnumerable<NamedSession> sessions)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var options = new List<string>();
        foreach (var session in sessions)
        {
            int n = seen[session.Name] = seen.GetValueOrDefault(session.Name) + 1;
            options.Add(n == 1 ? session.Name : $"{session.Name} ({n})");
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

    /// <summary>A name as stored: trimmed, capped, and the previous one where the edit is blank.</summary>
    public static string CleanName(string? typed, string previous)
    {
        string name = (typed ?? "").Trim();
        if (name.Length == 0) return previous;
        return name.Length > NameLength ? name[..NameLength].TrimEnd() : name;
    }
}
