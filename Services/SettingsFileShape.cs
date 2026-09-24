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

    public const string VersionKey = "Version";
    public const string ScreenKey  = "Screen";
    public const string FocusKey   = "Focus";

    /// <summary>The section names, in the order the document carries them. The store binds a section
    /// by its exact spelling, and one spelled in another case binds nothing and hands back
    /// defaults.</summary>
    public static readonly string[] SectionNames = [ScreenKey, FocusKey];

    /// <summary>First key in the file, so the shape is read rather than inferred.</summary>
    [JsonPropertyName(VersionKey), JsonPropertyOrder(0)]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName(ScreenKey), JsonPropertyOrder(1)]
    public ScreenGroup Screen { get; set; } = new();

    [JsonPropertyName(FocusKey), JsonPropertyOrder(2)]
    public FocusGroup Focus { get; set; } = new();

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
        [JsonPropertyOrder(2)] public bool? FocusDimsScreen         { get; set; }
        [JsonPropertyOrder(3)] public bool? FocusCoversScreen       { get; set; }
        [JsonPropertyOrder(4)] public bool? FocusStartFromDashboard { get; set; }
        // The running session. State rather than settings: nothing on the page edits these, so they
        // trail the visible rows.
        [JsonPropertyOrder(5)] public DateTimeOffset? FocusSessionStartedAt { get; set; }
        [JsonPropertyOrder(6)] public DateTimeOffset? FocusSessionEndsAt    { get; set; }
        [JsonPropertyOrder(7)] public bool FocusSessionDimmedScreen  { get; set; }
        [JsonPropertyOrder(8)] public bool FocusSessionCoveredScreen { get; set; }
    }

    public static SettingsFile From(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new SettingsFile
        {
            Screen = new ScreenGroup { ScreenSavedBrightness = s.ScreenSavedBrightness },
            Focus = new FocusGroup
            {
                FocusSessionMinutes       = s.FocusSessionMinutes,
                FocusDimsScreen           = s.FocusDimsScreen,
                FocusCoversScreen         = s.FocusCoversScreen,
                FocusStartFromDashboard   = s.FocusStartFromDashboard,
                FocusSessionStartedAt     = s.FocusSessionStartedAt,
                FocusSessionEndsAt        = s.FocusSessionEndsAt,
                FocusSessionDimmedScreen  = s.FocusSessionDimmedScreen,
                FocusSessionCoveredScreen = s.FocusSessionCoveredScreen,
            },
        };
    }

    public AppSettings ToSettings() => new()
    {
        ScreenSavedBrightness = Screen.ScreenSavedBrightness,

        FocusSessionMinutes       = Focus.FocusSessionMinutes ?? FocusSessionEngine.DefaultMinutes,
        FocusDimsScreen           = Focus.FocusDimsScreen ?? true,
        FocusCoversScreen         = Focus.FocusCoversScreen ?? true,
        FocusStartFromDashboard   = Focus.FocusStartFromDashboard ?? true,
        FocusSessionStartedAt     = Focus.FocusSessionStartedAt,
        FocusSessionEndsAt        = Focus.FocusSessionEndsAt,
        FocusSessionDimmedScreen  = Focus.FocusSessionDimmedScreen,
        FocusSessionCoveredScreen = Focus.FocusSessionCoveredScreen,
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
