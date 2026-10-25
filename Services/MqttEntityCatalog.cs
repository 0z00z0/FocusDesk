using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Services;

/// <summary>Everything the entity table reads and writes, in one object initialiser. Supplied by the
/// publisher at runtime and by a spy in a test, so the whole table composes with no broker, no
/// display and no settings file.</summary>
internal sealed record MqttEntitySources
{
    /// <summary>The settings and session snapshot, or null when it cannot be taken.</summary>
    public required Func<SurfaceState?> Surface { get; init; }

    /// <summary>The gates the announcement is filtered through. <b>Throws rather than answering</b>
    /// when a read fails: the announcement layer reads a throw as "could not be read" and keeps the
    /// disposition already recorded, while a false says the capability is absent and withholds every
    /// entity behind it.</summary>
    public required Func<PublishCapabilities> Capabilities { get; init; }

    public required ISettingsActions Settings { get; init; }
}

/// <summary>
/// FocusDesk's published surface: eleven entities, their groups, their capability gates and the domain
/// seam each inbound command lands on. Pure — nothing here touches a broker or a settings singleton,
/// so the same table composes in a test.
/// </summary>
/// <remarks>An entity id is the <c>unique_id</c> stem after the device id, so it carries the entity
/// across every rename, regrouping and topic move the receiver ever sees. Change one and the user
/// loses the name, the entity id, the area, the labels and the automations attached to it. They do
/// not change.</remarks>
internal static class MqttEntityCatalog
{
    /// <summary>The first segment of every topic this application publishes under. Part of the
    /// published surface: a receiver's automations and templates name it, so it does not change.</summary>
    public const string TopicRoot = "focusdesk";

    // Entity ids. Shared so the table and the tests name each entity identically.

    public const string ScreenBrightness        = "screen_brightness";
    public const string ScreenBrightnessRestore = "screen_brightness_restore";

    public const string FocusSession             = "focus_session";
    public const string FocusSessionMinutes      = "focus_session_minutes";
    public const string FocusSessionType         = "focus_session_type";
    public const string FocusSessionBlocksNetwork = "focus_session_blocks_network";
    public const string FocusSessionNamedSession = "focus_session_named_session";
    public const string FocusSessionDimsScreen   = "focus_session_dims_screen";
    public const string FocusSessionBlocksInput  = "focus_session_blocks_input";
    public const string FocusSessionState        = "focus_session_state";
    public const string FocusSessionRemaining    = "focus_session_remaining";

    public static MqttEntitySet Build(MqttEntitySources s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var surface = s.Surface;
        var set = s.Settings;

        return new MqttEntitySet(
        [
            // ── Screen ───────────────────────────────────────────────────────────────────────────
            new MqttNumber
            {
                EntityId = ScreenBrightness, Name = "Screen brightness", Group = MqttPublishGroups.Screen,
                Description = "Sets the brightness of the built-in display from 0 to 100 percent and "
                            + "remembers the level it had before the first change, so that the Screen "
                            + "brightness restore button can put it back.",
                Unit = "%", Icon = "mdi:brightness-6",
                Min = ScreenBrightnessPark.Minimum, Max = ScreenBrightnessPark.Maximum,
                Mode = MqttNumberMode.Slider, Debounce = MqttConnection.ReflectDebounce,
                Include = () => s.Capabilities().ScreenBrightness,
                Read = () => surface()?.ScreenBrightness,
                Apply = value => MqttCommandVerdict.Accept(
                    () => set.SetScreenBrightness(Whole(value), ScreenBrightness)),
            },
            new MqttButton
            {
                EntityId = ScreenBrightnessRestore, Name = "Screen brightness restore",
                Group = MqttPublishGroups.Screen, Icon = "mdi:brightness-auto",
                Description = "Puts the display brightness back to the level it had before the first "
                            + "change, and does nothing when no earlier level is remembered.",
                Include = () => s.Capabilities().ScreenBrightness,
                Press = () => MqttCommandVerdict.Accept(
                    () => set.RestoreScreenBrightness(ScreenBrightnessRestore)),
            },

            // ── Focus session ────────────────────────────────────────────────────────────────────
            // The only way out. Nothing on the machine ends a session, so these are the whole of
            // the feature's remote surface and the only surface that ends one.
            new MqttSwitch
            {
                // On arms a session; off asks to cancel one, which opens the staged wait rather than
                // ending it. The switch therefore stays on through a cancel attempt, and the state
                // reading below is what says how far that attempt has got.
                EntityId = FocusSession, Name = "Focus session", Group = MqttPublishGroups.Focus,
                Description = "Turning on starts a focus session with the type, length and restrictions "
                            + "currently set in the other Focus session entities, and does nothing if a "
                            + "session already runs or the session cannot be started. Turning off does "
                            + "not end the session at once: it starts a five-minute wait, after which "
                            + "turning off again within ten seconds ends the session, and the switch "
                            + "stays on until the session is over.",
                Icon = "mdi:meditation", Debounce = MqttConnection.ReflectDebounce,
                Read = () => surface() is { } v ? v.FocusStage != FocusSessionStage.Off : (bool?)null,
                Apply = on => MqttCommandVerdict.Accept(() => set.SetFocusSession(on, FocusSession)),
            },
            new MqttNumber
            {
                EntityId = FocusSessionMinutes, Name = "Focus session minutes",
                Group = MqttPublishGroups.Focus,
                Description = "Sets how many minutes the next focus session lasts, from 1 to 240; a "
                            + "change while a session runs applies to the next session only.",
                Category = MqttEntityCategory.Config, Unit = "min", Icon = "mdi:timer-outline",
                Min = FocusSessionEngine.MinMinutes, Max = FocusSessionEngine.MaxMinutes,
                Mode = MqttNumberMode.Box, Debounce = MqttConnection.ReflectDebounce,
                Read = () => surface()?.FocusSessionMinutes,
                Apply = value => MqttCommandVerdict.Accept(() => set.SetFocusSessionMinutes(Whole(value))),
            },
            new MqttSelect
            {
                // The kind the session switch starts. The option words are published vocabulary and
                // stay English whatever language the interface runs in. A write is refused while a
                // session runs, and the running kind reflects back through the debounce. Starting a
                // given kind is two calls in order: this select, then the session switch.
                EntityId = FocusSessionType, Name = "Focus session type",
                Group = MqttPublishGroups.Focus,
                Description = "Chooses whether the next focus session is Program focus, which keeps only "
                            + "the chosen programs usable, or Screen break, which covers every display; "
                            + "it cannot be changed while a session runs and then shows the type of the "
                            + "running session.",
                Category = MqttEntityCategory.Config, Icon = "mdi:shape-outline",
                Debounce = MqttConnection.ReflectDebounce,
                Options = () => FocusSessionKinds.Options,
                Read = () => surface() is { } v ? FocusSessionKinds.Option(v.FocusKind) : null,
                Apply = option => FocusSessionKinds.FromOption(option) is { } kind
                    ? MqttCommandVerdict.Accept(() => set.SetFocusSessionKind(kind))
                    : MqttCommandVerdict.NotAnOption($"'{option}' names no kind of session."),
            },
            new MqttSwitch
            {
                // Not gated: whether the firewall accepts the block is decided at arming time. A
                // refused block leaves the session running, and this reads off while it runs.
                EntityId = FocusSessionBlocksNetwork, Name = "Focus session blocks network",
                Group = MqttPublishGroups.Focus,
                Description = "Sets whether a screen break blocks all network traffic except the MQTT "
                            + "broker and the programs ticked Can use the network in the named session in "
                            + "use; while a session runs, it shows whether the block is held and a change "
                            + "is refused.",
                Category = MqttEntityCategory.Config, Icon = "mdi:lan-disconnect",
                Debounce = MqttConnection.ReflectDebounce,
                Read = () => surface()?.FocusBlocksNetwork,
                Apply = on => MqttCommandVerdict.Accept(() => set.SetFocusBlocksNetwork(on)),
            },
            new MqttSelect
            {
                // Options follow the named-session list; a write is refused while a session runs and the
                // running session's name reflects back. It never changes the kind.
                EntityId = FocusSessionNamedSession, Name = "Named session",
                Group = MqttPublishGroups.Focus,
                Description = "Chooses which named session, with its own programs, the next Program focus "
                            + "session uses; it cannot be changed while a session runs and then shows the "
                            + "name of the running session.",
                Category = MqttEntityCategory.Config, Icon = "mdi:playlist-check",
                Debounce = MqttConnection.ReflectDebounce,
                Options = () => surface()?.NamedSessionOptions ?? [],
                Read = () => surface()?.NamedSession,
                Apply = option => surface()?.NamedSessionOptions is { } options && !options.Contains(option)
                    ? MqttCommandVerdict.NotAnOption($"'{option}' names no named session.")
                    : MqttCommandVerdict.Accept(() => set.SetFocusNamedSession(option)),
            },
            new MqttSwitch
            {
                // Gated on the display, like the Screen page's own entities: announcing a lever a
                // machine cannot honour leaves the receiver with a switch that does nothing. The
                // value is the default the next screen break starts from, and a write is refused
                // while a session runs — the refused value reflects back through the debounce.
                EntityId = FocusSessionDimsScreen, Name = "Focus session dims screen",
                Group = MqttPublishGroups.Focus,
                Description = "Sets whether a screen break also lowers the display brightness to its "
                            + "minimum behind the black cover, which Program focus never does; while a "
                            + "session runs, it shows whether the screen is dimmed and a change is "
                            + "refused.",
                Category = MqttEntityCategory.Config, Icon = "mdi:brightness-2",
                Debounce = MqttConnection.ReflectDebounce,
                Include = () => s.Capabilities().ScreenBrightness,
                Read = () => surface()?.FocusDimsScreen,
                Apply = on => MqttCommandVerdict.Accept(() => set.SetFocusDimsScreen(on)),
            },
            new MqttSwitch
            {
                // Not gated on anything: a machine always has a mouse and keyboard to block, and
                // whether Windows accepts the block is decided at arming time. A refused block leaves
                // the session running, and this reads off while it runs. Only a screen break reads
                // it.
                EntityId = FocusSessionBlocksInput, Name = "Focus session blocks input",
                Group = MqttPublishGroups.Focus,
                Description = "Sets whether a screen break blocks the physical mouse and keyboard, which "
                            + "Program focus never does and which needs administrator rights; while a "
                            + "session runs, it shows whether the block is held and a change is refused.",
                Category = MqttEntityCategory.Config, Icon = "mdi:keyboard-off",
                Debounce = MqttConnection.ReflectDebounce,
                Read = () => surface()?.FocusBlocksInput,
                Apply = on => MqttCommandVerdict.Accept(() => set.SetFocusBlocksInput(on)),
            },
            MqttEnumSensor.Of(
                FocusSessionState, "Focus session state", MqttPublishGroups.Focus,
                MqttEntityCategory.Diagnostic, "mdi:progress-clock",
                FocusSessionStages.Words,
                () => surface() is { } v ? FocusSessionStages.Label(v.FocusStage) : null,
                description: "Shows Off when no focus session runs, Active while one runs, Ending during "
                           + "the five-minute wait after a cancel was asked for, and Confirm during the "
                           + "following ten seconds in which asking again ends the session."),
            new MqttSensor
            {
                // The countdown to the original end time, which a cancel attempt never pauses. A
                // live value a receiver watches, so it sorts in Sensors rather than in Diagnostic
                // with the state above.
                EntityId = FocusSessionRemaining, Name = "Focus session remaining",
                Group = MqttPublishGroups.Focus,
                Description = "Shows the minutes left of the running focus session, rounded up to whole "
                            + "minutes, and keeps counting towards the original end time while a cancel "
                            + "is waiting; the value is unknown when no session runs.",
                Category = MqttEntityCategory.Primary, DeviceClass = "duration", Unit = "min",
                Icon = "mdi:timer-sand",
                Read = () => MqttPayload.Number((long?)surface()?.FocusRemainingMinutes),
            },
        ]);
    }

    /// <summary>An inbound number as the whole one every FocusDesk setting is. Already inside the
    /// entity's declared bounds by the time it arrives, so this only drops a fractional part a
    /// receiver's own control could not have produced.</summary>
    private static int Whole(double value) => (int)Math.Round(value);
}
