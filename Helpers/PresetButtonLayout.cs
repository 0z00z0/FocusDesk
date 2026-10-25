namespace FocusDesk.Helpers;

/// <summary>Column arithmetic for the named-session buttons, copied from ChargeKeeper's Smart Charge
/// preset buttons (commit 5727ea5).</summary>
internal static class PresetButtonLayout
{
    // Narrow enough that five buttons share one row of the 364-unit pop-out; long names wrap.
    internal const double MinButtonWidth = 56;

    // The gap between every control in the status window's session card (SessionControlGap).
    internal const double Spacing = 6;

    internal static (int Columns, int Rows) Choose(int count, double availableWidth)
    {
        if (count <= 0) return (0, 0);

        int fit     = (int)((availableWidth + Spacing) / (MinButtonWidth + Spacing));
        int columns = Math.Clamp(fit, 1, count);
        return (columns, (count + columns - 1) / columns);
    }
}
