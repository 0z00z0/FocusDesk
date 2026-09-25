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
    public const int CurrentVersion = 1;

    public const string VersionKey    = "Version";
    public const string FocusKey      = "Focus";
    public const string ScreenKey     = "Screen";
    public const string AppearanceKey = "Appearance";
    public const string WindowKey     = "Window";

    /// <summary>The section names, in the order the document carries them. The store binds a section
    /// by its exact spelling, and one spelled in another case binds nothing and hands back
    /// defaults.</summary>
    public static readonly string[] SectionNames = [FocusKey, ScreenKey, AppearanceKey, WindowKey];

    /// <summary>First key in the file, so the shape is read rather than inferred.</summary>
    [JsonPropertyName(VersionKey), JsonPropertyOrder(0)]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName(FocusKey), JsonPropertyOrder(1)]
    public FocusGroup Focus { get; set; } = new();

    [JsonPropertyName(ScreenKey), JsonPropertyOrder(2)]
    public ScreenGroup Screen { get; set; } = new();

    [JsonPropertyName(AppearanceKey), JsonPropertyOrder(3)]
    public AppearanceGroup Appearance { get; set; } = new();

    /// <summary>Not a page: where the Settings window was last left. It trails the pages for that
    /// reason.</summary>
    [JsonPropertyName(WindowKey), JsonPropertyOrder(4)]
    public WindowGroup Window { get; set; } = new();

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
        [JsonPropertyOrder(2)] public bool? FocusBlocksNetwork      { get; set; }
        [JsonPropertyOrder(3)] public bool? FocusLimitsPrograms     { get; set; }
        // Null only in a document written before the list existed, which is what lets its path list
        // migrate. Written as an empty list rather than null, because the store never deletes a key:
        // the earlier list stays in the file and must never migrate a second time.
        [JsonPropertyOrder(4)] public List<FocusProgramEntry>? FocusPrograms { get; set; }
        [JsonPropertyOrder(5)] public FocusProgramAction? FocusProgramsDefaultAction { get; set; }
        [JsonPropertyOrder(6)] public bool? FocusDimsScreen         { get; set; }
        [JsonPropertyOrder(7)] public bool? FocusCoversScreen       { get; set; }
        [JsonPropertyOrder(8)] public bool? FocusBlocksInput        { get; set; }
        [JsonPropertyOrder(9)] public bool? FocusStartFromDashboard { get; set; }
        // The running session and the firewall state it displaced. State rather than settings:
        // nothing on the page edits these, so they trail the visible rows.
        [JsonPropertyOrder(10)] public DateTimeOffset? FocusSessionStartedAt { get; set; }
        [JsonPropertyOrder(11)] public DateTimeOffset? FocusSessionEndsAt    { get; set; }
        [JsonPropertyOrder(12)] public bool FocusSessionBlockedNetwork { get; set; }
        [JsonPropertyOrder(13)] public bool FocusSessionDimmedScreen   { get; set; }
        [JsonPropertyOrder(14)] public bool FocusSessionCoveredScreen  { get; set; }
        [JsonPropertyOrder(15)] public bool FocusSessionBlockedInput   { get; set; }
        [JsonPropertyOrder(16)] public bool FocusSessionLimitedPrograms { get; set; }
        [JsonPropertyOrder(17)] public List<FirewallProfileSetting>? FocusSavedFirewall { get; set; }

        // An earlier document's path list, read to migrate and never written: null on every write,
        // and a null key is left out. The store keeps the earlier bytes where they stand.
        [JsonPropertyOrder(18), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? FocusAllowedPrograms { get; set; }
    }

    /// <summary>Keys read from an earlier document and never written. They sit in a group so the
    /// store binds them, and no setting carries them.</summary>
    public static readonly string[] ReadOnlyKeys = [nameof(FocusGroup.FocusAllowedPrograms)];

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
            Screen = new ScreenGroup { ScreenSavedBrightness = s.ScreenSavedBrightness },
            Appearance = new AppearanceGroup
            {
                PromoteTrayIcon               = s.PromoteTrayIcon,
                TrayIconPromotionRestoreFor   = s.TrayIconPromotionRestoreFor,
                TrayIconPromotionRestoreValue = s.TrayIconPromotionRestoreValue,
            },
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
                FocusBlocksNetwork        = s.FocusBlocksNetwork,
                FocusLimitsPrograms       = s.FocusLimitsPrograms,
                FocusPrograms             = s.FocusPrograms,
                FocusProgramsDefaultAction = s.FocusProgramsDefaultAction,
                FocusDimsScreen           = s.FocusDimsScreen,
                FocusCoversScreen         = s.FocusCoversScreen,
                FocusBlocksInput          = s.FocusBlocksInput,
                FocusStartFromDashboard   = s.FocusStartFromDashboard,
                FocusSessionStartedAt     = s.FocusSessionStartedAt,
                FocusSessionEndsAt        = s.FocusSessionEndsAt,
                FocusSessionDimmedScreen  = s.FocusSessionDimmedScreen,
                FocusSessionCoveredScreen = s.FocusSessionCoveredScreen,
                FocusSessionBlockedInput  = s.FocusSessionBlockedInput,
                FocusSessionBlockedNetwork = s.FocusSessionBlockedNetwork,
                FocusSessionLimitedPrograms = s.FocusSessionLimitedPrograms,
                FocusSavedFirewall        = s.FocusSavedFirewall,
            },
        };
    }

    public AppSettings ToSettings() => new()
    {
        ScreenSavedBrightness = Screen.ScreenSavedBrightness,

        FocusSessionMinutes       = Focus.FocusSessionMinutes ?? FocusSessionEngine.DefaultMinutes,
        FocusBlocksNetwork        = Focus.FocusBlocksNetwork ?? false,
        FocusLimitsPrograms       = Focus.FocusLimitsPrograms ?? false,
        // A document with no program list but an earlier path list migrates; one with neither reads
        // as an empty list.
        FocusPrograms             = Focus.FocusPrograms ?? FocusAllowedPrograms.Migrate(Focus.FocusAllowedPrograms),
        FocusProgramsDefaultAction = Focus.FocusProgramsDefaultAction ?? FocusProgramAction.Minimise,
        FocusDimsScreen           = Focus.FocusDimsScreen ?? true,
        FocusCoversScreen         = Focus.FocusCoversScreen ?? true,
        FocusBlocksInput          = Focus.FocusBlocksInput ?? false,
        FocusStartFromDashboard   = Focus.FocusStartFromDashboard ?? true,
        FocusSessionStartedAt     = Focus.FocusSessionStartedAt,
        FocusSessionEndsAt        = Focus.FocusSessionEndsAt,
        FocusSessionDimmedScreen  = Focus.FocusSessionDimmedScreen,
        FocusSessionCoveredScreen = Focus.FocusSessionCoveredScreen,
        FocusSessionBlockedInput  = Focus.FocusSessionBlockedInput,
        FocusSessionBlockedNetwork = Focus.FocusSessionBlockedNetwork,
        FocusSessionLimitedPrograms = Focus.FocusSessionLimitedPrograms,
        FocusSavedFirewall        = Focus.FocusSavedFirewall,

        PromoteTrayIcon               = Appearance.PromoteTrayIcon ?? false,
        TrayIconPromotionRestoreFor   = Appearance.TrayIconPromotionRestoreFor,
        TrayIconPromotionRestoreValue = Appearance.TrayIconPromotionRestoreValue,

        SettingsWindowX      = Window.SettingsWindowX,
        SettingsWindowY      = Window.SettingsWindowY,
        SettingsWindowWidth  = Window.SettingsWindowWidth,
        SettingsWindowHeight = Window.SettingsWindowHeight,
    };

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
