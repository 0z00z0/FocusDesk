using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The session lengths offered, and the list box that offers them. One list for both surfaces that
/// ask for a length — the Focus settings page and the status window's start box — so the two can
/// never offer different choices.
/// </summary>
internal static class FocusLengthChoices
{
    /// <summary>The lengths offered, in the order they are offered.</summary>
    private static readonly (string Label, int Value)[] Presets =
    [
        ("15 min", 15), ("25 min", 25), ("45 min", 45), ("1 hour", 60),
        ("90 min", 90), ("2 hours", 120), ("3 hours", 180), ("4 hours", 240),
    ];

    /// <summary>
    /// Fills a list with the lengths and selects <paramref name="stored"/>, adding it as a choice of
    /// its own where it is not one of the presets.
    /// </summary>
    /// <remarks>A duration set from Home Assistant can be any whole number of minutes, so one that
    /// is not offered is added in its place in the order rather than rounded to a neighbour.</remarks>
    public static void Fill(ComboBox combo, int stored)
    {
        ArgumentNullException.ThrowIfNull(combo);

        combo.Items.Clear();

        var offered = Presets.ToList();
        if (!offered.Any(p => p.Value == stored))
        {
            int at = offered.FindIndex(p => p.Value > stored);
            offered.Insert(at < 0 ? offered.Count : at, ($"{stored} min", stored));
        }

        foreach (var (label, value) in offered)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });

        combo.SelectedIndex = offered.FindIndex(p => p.Value == stored);
    }

    /// <summary>The length a list is showing, or null where nothing is selected.</summary>
    public static int? Selected(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        return combo.SelectedItem is ComboBoxItem { Tag: int minutes } ? minutes : null;
    }
}
