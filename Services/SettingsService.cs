namespace FocusDesk.Services;

/// <summary>Persisted application settings.</summary>
internal sealed class AppSettings
{
    /// <summary>The display brightness in force before the first change this application made, so it
    /// can be put back exactly — including after a run that ended without restoring it. Null means
    /// nothing is owed back. Written by the brightness park's own record alone.</summary>
    public int? ScreenSavedBrightness { get; set; }

    /// <summary>The duration the next focus session runs for, in minutes. A session carries one
    /// duration and there is no indefinite one: with no local way out, the duration is the whole
    /// backstop against a session that never ends.</summary>
    public int FocusSessionMinutes { get; set; } = FocusSessionEngine.DefaultMinutes;

    /// <summary>Whether the next focus session dims the screen. A default the session starts from,
    /// not a standing state: the choice is made per session from Home Assistant.</summary>
    public bool FocusDimsScreen { get; set; } = true;

    /// <summary>Whether the next focus session covers every display with a black window. Dimming to
    /// the panel's floor still leaves enough glow to read by, which is what this lever answers.</summary>
    public bool FocusCoversScreen { get; set; } = true;

    /// <summary>Whether the status window offers a control that starts a session. It never offers one
    /// that ends a session, whatever this holds: nothing on the machine ends one.</summary>
    public bool FocusStartFromDashboard { get; set; } = true;

    /// <summary>When the running focus session was armed. Only the cover's countdown ring reads it,
    /// to know what a full ring means; nothing about ending a session depends on it.</summary>
    public DateTimeOffset? FocusSessionStartedAt { get; set; }

    /// <summary>When the running focus session ends, or null when none is running. The session is
    /// defined by this instant rather than by a countdown, so a machine switched off mid-session
    /// still ends it — at the next start if the instant has already passed.</summary>
    public DateTimeOffset? FocusSessionEndsAt { get; set; }

    /// <summary>Which levers the running session owns, so only what it displaced is put back.
    /// Meaningless without <see cref="FocusSessionEndsAt"/>.</summary>
    public bool FocusSessionDimmedScreen { get; set; }

    /// <inheritdoc cref="FocusSessionDimmedScreen"/>
    public bool FocusSessionCoveredScreen { get; set; }

    /// <summary>Whether the notification-area icon is asked to sit in the main tray rather than in
    /// the overflow flyout. Windows offers no supported way to ask, so this is honoured by writing
    /// the shell's own undocumented setting and can silently do nothing.</summary>
    public bool PromoteTrayIcon { get; set; }

    /// <summary>Which icon the restore record below belongs to, as a GUID, or null where no record
    /// has been taken. A record made for a different icon is ignored rather than applied to this
    /// one.</summary>
    public string? TrayIconPromotionRestoreFor { get; set; }

    /// <summary>What the shell's own setting held before it was first written: true, false, or null
    /// where it held nothing at all. Null with a record present is what makes a restore delete the
    /// value rather than write a zero over it.</summary>
    public bool? TrayIconPromotionRestoreValue { get; set; }

    /// <summary>The Settings window's outer rectangle in physical pixels, as the window manager
    /// last reported it. All four are set together or none is: a partial rectangle is treated as
    /// nothing saved, and the window opens centred on the monitor under the cursor.</summary>
    public int? SettingsWindowX { get; set; }

    /// <inheritdoc cref="SettingsWindowX"/>
    public int? SettingsWindowY { get; set; }

    /// <inheritdoc cref="SettingsWindowX"/>
    public int? SettingsWindowWidth { get; set; }

    /// <inheritdoc cref="SettingsWindowX"/>
    public int? SettingsWindowHeight { get; set; }
}

/// <summary>Loads and saves <see cref="AppSettings"/> to <c>%AppData%\FocusDesk\settings.json</c> —
/// roaming AppData, so the file follows the user between machines on one profile. The document
/// itself is held by <see cref="SettingsStore"/>; this class owns the in-memory copy, the lock and
/// the notifications.</summary>
internal static class SettingsService
{
    private static readonly string _path = AppPaths.DataFile("settings.json");

    private static readonly Lock _lock = new();
    private static AppSettings? _current;

    /// <summary>Latches the save-failure notice, so a document that stays unwritable reports once
    /// rather than on every keystroke. Cleared by the first save that lands.</summary>
    private static bool _saveFailureReported;

    public static AppSettings Current
    {
        get { lock (_lock) { return _current ??= ReadFrom(_path) ?? new AppSettings(); } }
    }

    public static string FilePath => _path;

    /// <summary>Projects a value out of <see cref="Current"/> under the lock. Needed for anything
    /// that enumerates a collection: <see cref="Update"/> mutates in place, so an unsynchronised
    /// reader can throw "collection was modified".</summary>
    public static T Read<T>(Func<AppSettings, T> project)
    {
        ArgumentNullException.ThrowIfNull(project);
        lock (_lock) { return project(_current ??= ReadFrom(_path) ?? new AppSettings()); }
    }

    /// <summary>Writes <see cref="Current"/> to disk. Safe to call from any thread. A write that
    /// cannot land raises <see cref="SaveFailed"/>, because a setting that never reached the file
    /// looks saved on screen and is gone at the next start. Returns whether it landed.</summary>
    public static bool Save()
    {
        bool report, saved;
        lock (_lock)
        {
            saved = WriteTo(_current ?? new AppSettings(), _path);
            report = !saved && !_saveFailureReported;
            _saveFailureReported = !saved;
        }
        // Outside the lock — a subscriber shows a notification, which is a synchronous WinRT call.
        if (report) SaveFailed?.Invoke();
        return saved;
    }

    /// <summary>Raised the first time a save does not reach disk, and not again until one does. A
    /// refused write is returned rather than thrown, so nothing else would say the settings on
    /// screen are not the settings on disk.</summary>
    public static event Action? SaveFailed;

    /// <summary>Writes one settings object to one document, and reports whether every section
    /// landed. Separated from <see cref="Save"/> so the write can be exercised against a real file
    /// without touching the installed <c>settings.json</c>.</summary>
    /// <remarks>Never throws: callers are settings handlers with nothing to unwind.</remarks>
    internal static bool WriteTo(AppSettings settings, string path) =>
        SettingsStore.For(path).Write(settings);

    /// <summary>Reads, mutates and saves under one lock acquisition. Prefer this over mutating
    /// <see cref="Current"/> and calling <see cref="Save"/> separately — a <see cref="Reload"/>
    /// between the two silently drops the write. Returns whether the document landed on disk.</summary>
    public static bool Update(Action<AppSettings> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        bool saved;
        lock (_lock)
        {
            mutate(_current ??= ReadFrom(_path) ?? new AppSettings());
            saved = Save();   // re-entrant on the same Lock, so nesting does not deadlock
        }
        // Outside the lock — a subscriber may do real work.
        Changed?.Invoke();
        return saved;
    }

    /// <summary>Raised after any committed change, whatever moved.</summary>
    public static event Action? Changed;

    /// <summary>The settings one document carries, or null when there is nothing usable: no file, a
    /// document from a newer build, or one whose section spelling this build cannot bind. A document
    /// that cannot be parsed is set aside by the store before defaults replace it.</summary>
    internal static AppSettings? ReadFrom(string path) => SettingsStore.For(path).Read();

    /// <summary>Re-reads settings.json into <see cref="Current"/>, discarding unsaved changes, so an
    /// out-of-band edit is picked up without a restart. Returns false and leaves
    /// <see cref="Current"/> untouched on a missing or invalid file; never writes back.</summary>
    public static bool Reload()
    {
        if (ReadFrom(_path) is not { } loaded) return false;

        lock (_lock) _current = loaded;

        // Outside the lock — a subscriber may do real work.
        Reloaded?.Invoke();
        return true;
    }

    /// <summary>Services holding their own copy of a setting must reconcile here, or they keep
    /// running on the pre-reload value.</summary>
    public static event Action? Reloaded;
}
