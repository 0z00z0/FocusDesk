using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>
/// The on-disk shape of <c>settings.json</c>: one object per Settings page, in the order the pages
/// run in the navigation pane, and within each object the order the rows appear on that page.
/// Finding a setting in the file means looking where it sits on screen.
/// </summary>
/// <remarks>
/// A serialisation shape rather than the in-memory model: <see cref="AppSettings"/> stays flat, so
/// no call site changes and the grouping cannot drift into behaviour. Group keys are PascalCase, the
/// shape System.Text.Json produces with no naming policy applied, and are the file's own vocabulary:
/// nothing outside reads them, so they borrow nothing from the MQTT group names. Property order is
/// pinned with <see cref="JsonPropertyOrderAttribute"/> on every member: System.Text.Json orders
/// unattributed members by reflection order, which is not a guarantee.
/// </remarks>
internal sealed class SettingsFile
{
    /// <summary>2 since the named sessions, so a version 1 build leaves a document it cannot read alone.</summary>
    public const int CurrentVersion = 2;

    public const string VersionKey    = "Version";
    public const string FocusKey      = "Focus";
    public const string ProgramFocusKey = "ProgramFocus";
    public const string ScreenKey     = "Screen";
    public const string AppearanceKey = "Appearance";
    public const string AboutKey      = "About";
    public const string WindowKey     = "Window";

    /// <summary>The section names, in the order the document carries them. The store binds a section
    /// by its exact spelling, and one spelled in another case binds nothing and hands back
    /// defaults.</summary>
    public static readonly string[] SectionNames =
        [FocusKey, ProgramFocusKey, ScreenKey, AppearanceKey, AboutKey, WindowKey];

    /// <summary>First key in the file, so the shape is read rather than inferred.</summary>
    [JsonPropertyName(VersionKey), JsonPropertyOrder(0)]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName(FocusKey), JsonPropertyOrder(1)]
    public FocusGroup Focus { get; set; } = new();

    [JsonPropertyName(ProgramFocusKey), JsonPropertyOrder(2)]
    public ProgramFocusGroup ProgramFocus { get; set; } = new();

    [JsonPropertyName(ScreenKey), JsonPropertyOrder(3)]
    public ScreenGroup Screen { get; set; } = new();

    [JsonPropertyName(AppearanceKey), JsonPropertyOrder(4)]
    public AppearanceGroup Appearance { get; set; } = new();

    [JsonPropertyName(AboutKey), JsonPropertyOrder(5)]
    public AboutGroup About { get; set; } = new();

    /// <summary>Not a page: where the Settings window was last left. It trails the pages for that
    /// reason.</summary>
    [JsonPropertyName(WindowKey), JsonPropertyOrder(6)]
    public WindowGroup Window { get; set; } = new();

    internal sealed class ProgramFocusGroup
    {
        // Null only before the named sessions existed, which is what carries the Focus list into "Default".
        [JsonPropertyOrder(1)] public List<NamedSession>? NamedSessions { get; set; }
        [JsonPropertyOrder(2)] public string? SelectedNamedSession { get; set; }
    }

    internal sealed class ScreenGroup
    {
        // The brightness displaced by a dim, waiting to be put back. State rather than a setting:
        // nothing on the page edits it, and null means the display carries its own level.
        [JsonPropertyOrder(1)] public int? ScreenSavedBrightness { get; set; }
    }

    internal sealed class FocusGroup
    {
        // Nullable so a document written before the page existed reads the application's own
        // defaults rather than zero minutes and two levers switched off — a session that could
        // never be armed.
        [JsonPropertyOrder(1)] public int?  FocusSessionMinutes     { get; set; }
        [JsonPropertyOrder(2)] public bool? FocusStartFromDashboard { get; set; }
        [JsonPropertyOrder(3)] public bool? FocusPointShown         { get; set; }
        [JsonPropertyOrder(4)] public bool? FocusPointStartsSession { get; set; }
        [JsonPropertyOrder(5)] public int?  FocusPointSeconds       { get; set; }
        [JsonPropertyOrder(6)] public FocusSessionKind? FocusSessionKind { get; set; }
        [JsonPropertyOrder(7)] public bool? FocusBlocksNetwork      { get; set; }
        // Read once into "Default" and never written; the store keeps the earlier bytes where they stand.
        [JsonPropertyOrder(8), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FocusProgramEntry>? FocusPrograms { get; set; }
        [JsonPropertyOrder(9)] public FocusProgramAction? FocusProgramsDefaultAction { get; set; }
        [JsonPropertyOrder(10)] public bool? FocusDimsScreen        { get; set; }
        // The cover's visual, written as its name rather than as a number: a document a person opens
        // reads "ring" or "focus-point", and a name unknown to this build falls back to the dial.
        [JsonPropertyOrder(11)] public string? FocusCoverVisual     { get; set; }
        [JsonPropertyOrder(12)] public bool? FocusBlocksInput       { get; set; }
        // The running session and the firewall state it displaced. State rather than settings:
        // nothing on the page edits these, so they trail the visible rows.
        [JsonPropertyOrder(13)] public DateTimeOffset? FocusSessionStartedAt { get; set; }
        [JsonPropertyOrder(14)] public DateTimeOffset? FocusSessionEndsAt    { get; set; }
        [JsonPropertyOrder(15)] public FocusSessionKind FocusSessionRunningKind { get; set; }
        [JsonPropertyOrder(16)] public bool FocusSessionBlockedNetwork { get; set; }
        [JsonPropertyOrder(17)] public bool FocusSessionDimmedScreen   { get; set; }
        [JsonPropertyOrder(18)] public bool FocusSessionCoveredScreen  { get; set; }
        [JsonPropertyOrder(19)] public bool FocusSessionBlockedInput   { get; set; }
        [JsonPropertyOrder(20)] public bool FocusSessionLimitedPrograms { get; set; }
        [JsonPropertyOrder(21)] public string? FocusSessionGoal { get; set; }
        [JsonPropertyOrder(22)] public string? FocusSessionNamedSession { get; set; }
        [JsonPropertyOrder(23)] public List<FirewallProfileSetting>? FocusSavedFirewall { get; set; }

        // An earlier document's path list, read to migrate and never written: null on every write,
        // and a null key is left out. The store keeps the earlier bytes where they stand.
        [JsonPropertyOrder(24), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FocusAllowedPrograms { get; set; }
    }

    /// <summary>Keys read from an earlier document and never written. They sit in a group so the
    /// store binds them, and no setting carries them.</summary>
    public static readonly string[] ReadOnlyKeys =
    [
        nameof(FocusGroup.FocusAllowedPrograms), nameof(FocusGroup.FocusPrograms),
    ];

    internal sealed class AppearanceGroup
    {
        // Nullable so a document written before the page existed reads the application's own default
        // rather than a choice nobody made.
        [JsonPropertyOrder(1)] public bool? PromoteTrayIcon { get; set; }
        // State rather than a setting: what the shell held before the row was first switched on, so
        // switching it off puts that back. Nothing on the page edits these, so they trail the row.
        [JsonPropertyOrder(2)] public string? TrayIconPromotionRestoreFor   { get; set; }
        [JsonPropertyOrder(3)] public bool?   TrayIconPromotionRestoreValue { get; set; }
    }

    internal sealed class AboutGroup
    {
        // Nullable so a document written before the row existed reads off, the component's own
        // default, rather than a choice nobody made.
        [JsonPropertyOrder(1)] public bool? InstallUpdatesUnattended { get; set; }
    }

    internal sealed class WindowGroup
    {
        [JsonPropertyOrder(1)] public int? SettingsWindowX      { get; set; }
        [JsonPropertyOrder(2)] public int? SettingsWindowY      { get; set; }
        [JsonPropertyOrder(3)] public int? SettingsWindowWidth  { get; set; }
        [JsonPropertyOrder(4)] public int? SettingsWindowHeight { get; set; }
    }

    public static SettingsFile From(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new SettingsFile
        {
            ProgramFocus = new ProgramFocusGroup
            {
                NamedSessions        = s.NamedSessions,
                SelectedNamedSession = s.SelectedNamedSession,
            },
            Screen = new ScreenGroup { ScreenSavedBrightness = s.ScreenSavedBrightness },
            Appearance = new AppearanceGroup
            {
                PromoteTrayIcon               = s.PromoteTrayIcon,
                TrayIconPromotionRestoreFor   = s.TrayIconPromotionRestoreFor,
                TrayIconPromotionRestoreValue = s.TrayIconPromotionRestoreValue,
            },
            About = new AboutGroup { InstallUpdatesUnattended = s.InstallUpdatesUnattended },
            Window = new WindowGroup
            {
                SettingsWindowX      = s.SettingsWindowX,
                SettingsWindowY      = s.SettingsWindowY,
                SettingsWindowWidth  = s.SettingsWindowWidth,
                SettingsWindowHeight = s.SettingsWindowHeight,
            },
            Focus = new FocusGroup
            {
                FocusSessionMinutes       = s.FocusSessionMinutes,
                FocusSessionKind          = s.FocusSessionKind,
                FocusBlocksNetwork        = s.FocusBlocksNetwork,
                FocusProgramsDefaultAction = s.FocusProgramsDefaultAction,
                FocusDimsScreen           = s.FocusDimsScreen,
                FocusCoverVisual          = CoverAppearance.NameOf(s.FocusCoverVisual),
                FocusBlocksInput          = s.FocusBlocksInput,
                FocusStartFromDashboard   = s.FocusStartFromDashboard,
                FocusPointShown           = s.FocusPointShown,
                FocusPointStartsSession   = s.FocusPointStartsSession,
                FocusPointSeconds         = s.FocusPointSeconds,
                FocusSessionStartedAt     = s.FocusSessionStartedAt,
                FocusSessionEndsAt        = s.FocusSessionEndsAt,
                FocusSessionRunningKind   = s.FocusSessionRunningKind,
                FocusSessionDimmedScreen  = s.FocusSessionDimmedScreen,
                FocusSessionCoveredScreen = s.FocusSessionCoveredScreen,
                FocusSessionBlockedInput  = s.FocusSessionBlockedInput,
                FocusSessionBlockedNetwork = s.FocusSessionBlockedNetwork,
                FocusSessionLimitedPrograms = s.FocusSessionLimitedPrograms,
                FocusSessionGoal          = s.FocusSessionGoal,
                FocusSessionNamedSession  = s.FocusSessionNamedSession,
                FocusSavedFirewall        = s.FocusSavedFirewall,
            },
        };
    }

    /// <summary>Whether reading this document creates or repairs named sessions, so the reader writes
    /// it back at once and every key it hands out stays the one on disk.</summary>
    public bool NamedSessionsNeedWriting =>
        ProgramFocus.NamedSessions is not { Count: > 0 } sessions
        || sessions.Any(n => n is null || string.IsNullOrWhiteSpace(n.Key) || string.IsNullOrWhiteSpace(n.Name));

    /// <summary>The list as read. Absent, today's Focus list becomes "Default"; empty, "Default" holds
    /// nothing; a session missing its key or name gets one.</summary>
    private List<NamedSession> ReadNamedSessions()
    {
        if (ProgramFocus.NamedSessions is null)
            return [NamedSession.Create(NamedSessions.DefaultName(),
                                        Focus.FocusPrograms ?? FocusAllowedPrograms.Migrate(Focus.FocusAllowedPrograms))];

        var sessions = ProgramFocus.NamedSessions.Where(n => n is not null).ToList();
        foreach (var session in sessions)
        {
            if (string.IsNullOrWhiteSpace(session.Key)) session.Key = NamedSessions.NewKey();
            if (string.IsNullOrWhiteSpace(session.Name)) session.Name = NamedSessions.DefaultName();
            session.Programs ??= [];
        }
        return sessions.Count > 0 ? sessions : [NamedSession.Create(NamedSessions.DefaultName())];
    }

    public AppSettings ToSettings() => WithNamedSessions(new()
    {
        ScreenSavedBrightness = Screen.ScreenSavedBrightness,

        FocusSessionMinutes       = Focus.FocusSessionMinutes ?? FocusSessionEngine.DefaultMinutes,
        // Nothing carries over from the two lever switches the kind replaced: an installation that
        // has never chosen a kind gets a screen break.
        FocusSessionKind          = Focus.FocusSessionKind ?? FocusSessionKind.ScreenBreak,
        FocusBlocksNetwork        = Focus.FocusBlocksNetwork ?? false,
        FocusProgramsDefaultAction = Focus.FocusProgramsDefaultAction ?? FocusProgramAction.Minimise,
        FocusDimsScreen           = Focus.FocusDimsScreen ?? true,
        FocusCoverVisual          = CoverAppearance.ParseVisual(Focus.FocusCoverVisual),
        FocusBlocksInput          = Focus.FocusBlocksInput ?? true,
        FocusStartFromDashboard   = Focus.FocusStartFromDashboard ?? true,
        FocusPointShown           = Focus.FocusPointShown ?? true,
        FocusPointStartsSession   = Focus.FocusPointStartsSession ?? false,
        FocusPointSeconds         = FocusPointStart.SecondsOrDefault(Focus.FocusPointSeconds),
        FocusSessionStartedAt     = Focus.FocusSessionStartedAt,
        FocusSessionEndsAt        = Focus.FocusSessionEndsAt,
        FocusSessionRunningKind   = Focus.FocusSessionRunningKind,
        FocusSessionDimmedScreen  = Focus.FocusSessionDimmedScreen,
        FocusSessionCoveredScreen = Focus.FocusSessionCoveredScreen,
        FocusSessionBlockedInput  = Focus.FocusSessionBlockedInput,
        FocusSessionBlockedNetwork = Focus.FocusSessionBlockedNetwork,
        FocusSessionLimitedPrograms = Focus.FocusSessionLimitedPrograms,
        FocusSessionGoal          = Focus.FocusSessionGoal,
        FocusSessionNamedSession  = Focus.FocusSessionNamedSession,
        FocusSavedFirewall        = Focus.FocusSavedFirewall,

        PromoteTrayIcon               = Appearance.PromoteTrayIcon ?? false,
        TrayIconPromotionRestoreFor   = Appearance.TrayIconPromotionRestoreFor,
        TrayIconPromotionRestoreValue = Appearance.TrayIconPromotionRestoreValue,

        InstallUpdatesUnattended = About.InstallUpdatesUnattended ?? false,

        SettingsWindowX      = Window.SettingsWindowX,
        SettingsWindowY      = Window.SettingsWindowY,
        SettingsWindowWidth  = Window.SettingsWindowWidth,
        SettingsWindowHeight = Window.SettingsWindowHeight,
    });

    private AppSettings WithNamedSessions(AppSettings settings)
    {
        settings.NamedSessions = ReadNamedSessions();
        settings.SelectedNamedSession = ProgramFocus.NamedSessions is null
            ? settings.NamedSessions[0].Key
            : ProgramFocus.SelectedNamedSession;
        return settings;
    }

    /// <summary>The shape of a file, read from its version key rather than inferred from which keys
    /// it happens to carry. Null means the key is absent or not a whole number.</summary>
    public static int? ReadVersion(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(VersionKey, out var v) &&
        v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out int version)
            ? version
            : null;
}
