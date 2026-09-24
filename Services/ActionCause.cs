namespace FocusDesk.Services;

/// <summary>
/// Why an action was taken, as the line recording it says so. One factory per trigger, each taking
/// that trigger's own identity, so a line names the entity or the control that decided rather than
/// the category it belongs to.
/// </summary>
/// <remarks>
/// <para>The value cannot be built from an arbitrary phrase through a constructor: a factory is the
/// route, and its signature is what forces the identity in. Two spellings for one trigger — "Home
/// Assistant" beside "MQTT command" — are what a free-text parameter produces.</para>
/// <para>The implicit conversion from a phrase exists for the condition a state machine has already
/// worked out and already words well ("the session ran its length"). Such a phrase is a cause in its
/// own right and needs no factory; a trigger arriving from outside does.</para>
/// </remarks>
internal readonly record struct ActionCause
{
    /// <summary>What a cause reads as where a call site supplied none. Visible rather than empty: a
    /// clause ending in nothing reads as a formatting fault rather than as a missing cause.</summary>
    internal const string Unrecorded = "an unrecorded trigger";

    /// <summary>The separator between what happened and why. Shared by every sink so the two trails
    /// cannot drift apart.</summary>
    internal const string Separator = " — cause: ";

    private readonly string? _text;

    private ActionCause(string text) => _text = text;

    public override string ToString() => _text is { Length: > 0 } text ? text : Unrecorded;

    /// <summary>The cause as a clause appended to a sentence, so the line stays one sentence.</summary>
    public string Clause => Separator + ToString();

    /// <summary>A condition the caller has already worked out and worded. Not a way round the
    /// factories: a trigger arriving from outside the application has one of its own.</summary>
    public static implicit operator ActionCause(string phrase) => new(phrase);

    /// <summary>A command from Home Assistant, naming the entity it arrived on.</summary>
    public static ActionCause HomeAssistant(string entityId) =>
        new($"Home Assistant, on the '{entityId}' entity");

    /// <summary>A control on the Settings window, naming its page.</summary>
    public static ActionCause SettingsPage(string page) =>
        new($"the {page} page of the Settings window");

    /// <summary>Something a run that ended without tidying up left behind, put back at startup.</summary>
    public static ActionCause StartupRestore(string what) =>
        new($"{what} left by a previous run, put back at startup");

    /// <summary>The application starting.</summary>
    public static ActionCause Startup() => new("the application starting");

    /// <summary>The application closing. Deliberately not called a shutdown: that reads as the
    /// machine being shut down.</summary>
    public static ActionCause ApplicationClosing() => new("the application closing");
}
