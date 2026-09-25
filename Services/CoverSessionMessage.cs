using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>
/// What the cover tells the bundled focus-point page about the running session, once a second.
/// </summary>
/// <remarks>
/// <para>A published contract in both directions: the page reads these names and the names are all
/// it reads, so a rename here is a rename in <c>Assets\FocusPoint\focus-point.html</c> as well. The
/// page counts between messages from the length and the remainder, so a message that stops arriving
/// leaves it running down rather than frozen.</para>
/// <para><c>dim</c> asks the page for its quieter palette. It is the cover holding itself back, which
/// happens when the screen lever is <em>not</em> dimming the display — the word points the opposite
/// way from the lever's name.</para>
/// <para>The page's own text is Norwegian, which is what it shows opened on its own. Inside the cover
/// the interface language's text is sent with every message instead.</para>
/// </remarks>
internal static class CoverSessionMessage
{
    /// <summary>The one message type the page accepts. Anything else is ignored there.</summary>
    internal const string Type = "focus-session";

    /// <summary>The message for one reading, as the JSON the page is posted.</summary>
    /// <param name="hint">The line under the point while the session runs.</param>
    /// <param name="done">The line shown when the countdown reaches its end.</param>
    internal static string Compose(FocusCoverReading reading, CoverAppearance appearance,
                                   string hint, string done) =>
        JsonSerializer.Serialize(new Payload
        {
            Type = Type,
            // A session with no recorded length would leave the page dividing by nothing; it reads a
            // whole ring from a length equal to what is left.
            TotalSeconds     = Math.Round(Math.Max(reading.TotalSeconds, reading.SecondsLeft), 1),
            RemainingSeconds = Math.Round(reading.SecondsLeft, 1),
            Dim              = appearance.IsHeldBack,
            HintText         = hint,
            DoneText         = done,
        });

    private sealed class Payload
    {
        [JsonPropertyName("type")]             public string Type { get; init; } = "";
        [JsonPropertyName("totalSeconds")]     public double TotalSeconds { get; init; }
        [JsonPropertyName("remainingSeconds")] public double RemainingSeconds { get; init; }
        [JsonPropertyName("dim")]              public bool Dim { get; init; }
        [JsonPropertyName("hintText")]         public string HintText { get; init; } = "";
        [JsonPropertyName("doneText")]         public string DoneText { get; init; } = "";
    }
}
