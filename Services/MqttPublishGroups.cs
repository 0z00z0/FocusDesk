using ZeroZero.Mqtt;

namespace FocusDesk.Services;

/// <summary>The publishing groups the MQTT page toggles, one per Settings page.</summary>
/// <remarks>The keys are persisted, per key and never per index, so inserting or reordering a group
/// cannot move a user's choices onto a different one. A key is therefore as permanent as an entity
/// id: rename the label freely, never the key.</remarks>
internal static class MqttPublishGroups
{
    public const string Screen = "screen";
    public const string Focus  = "focus";

    /// <summary>The declarations the panel renders one row per, in the order the Settings pages run.</summary>
    public static IReadOnlyList<PublishGroup> Declared { get; } =
    [
        new(Screen, "Screen",
            Info: "The display brightness, and the button that puts back what it was."),
        // The one group whose entities are the only way in or out: a focus session is cancelled from
        // here and from nowhere else, so switching this off leaves the feature with no way to end a
        // session but its own clock.
        new(Focus, "Focus session",
            Info: "The session switch, how long it runs, which levers it uses, and how far a cancel "
                + "has got."),
    ];
}
