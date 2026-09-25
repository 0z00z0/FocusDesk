namespace FocusDesk.Services;

/// <summary>Which of the cover's two visuals a session draws.</summary>
internal enum CoverVisual
{
    /// <summary>The gauge: a tick dial counting the session down, on black.</summary>
    Ring,

    /// <summary>The breathing focus point, a bundled page drawn by an embedded browser.</summary>
    FocusPoint,
}

/// <summary>
/// How the cover presents itself for one session: which visual it draws, and how brightly.
/// </summary>
/// <remarks>
/// <para>The intensity answers the screen lever rather than the time of day. A cover over a display
/// the session has dimmed draws at full strength, because the panel is already at its floor. A cover
/// over a display nobody asked to have dimmed holds itself back instead, so the cover does not become
/// the brightest thing on a screen that was left alone.</para>
/// <para>Held back means a lower opacity over black, which on a black background is the same
/// measurement as a lower luminance: one knob rather than a second palette to keep in step.</para>
/// </remarks>
internal readonly record struct CoverAppearance(CoverVisual Visual, double Intensity)
{
    internal const double FullIntensity = 1.0;

    /// <summary>Held back far enough to read as quieter without going unreadable at a panel's
    /// floor.</summary>
    internal const double HeldBackIntensity = 0.55;

    /// <summary>The names the settings document carries. Spelled out rather than taken from the enum
    /// so renaming a member does not silently orphan every stored document.</summary>
    internal const string RingName = "ring";

    internal const string FocusPointName = "focus-point";

    internal static CoverAppearance For(CoverVisual visual, bool screenIsDimmed) =>
        new(visual, screenIsDimmed ? FullIntensity : HeldBackIntensity);

    /// <summary>Whether the cover is drawing quieter than it can.</summary>
    internal bool IsHeldBack => Intensity < FullIntensity;

    /// <summary>The visual a stored name asks for. Anything unrecognised — an empty key, a document
    /// from a build that offered a third visual — reads as the ring, which needs nothing installed to
    /// draw.</summary>
    internal static CoverVisual ParseVisual(string? stored) =>
        string.Equals(stored?.Trim(), FocusPointName, StringComparison.OrdinalIgnoreCase)
            ? CoverVisual.FocusPoint
            : CoverVisual.Ring;

    /// <summary>The name a visual is stored under.</summary>
    internal static string NameOf(CoverVisual visual) =>
        visual == CoverVisual.FocusPoint ? FocusPointName : RingName;
}
