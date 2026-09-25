using Windows.UI.ViewManagement;

namespace FocusDesk.Helpers;

/// <summary>Whether this machine wants animation at all.</summary>
/// <remarks>Reads the same accessibility setting a browser maps <c>prefers-reduced-motion</c> from —
/// Settings, Accessibility, Visual effects, Animation effects — so the cover's two visuals answer one
/// switch rather than disagreeing. Read when a cover is built, so turning it off takes effect at the
/// next session rather than mid-session.</remarks>
internal static class MotionPreference
{
    /// <summary>True unless the setting is off. A reading that fails is treated as on: an animation
    /// nobody asked to lose is a smaller fault than a cover that sits still for no reason.</summary>
    internal static bool AnimationsAllowed()
    {
        try { return new UISettings().AnimationsEnabled; }
        catch { return true; }
    }
}
