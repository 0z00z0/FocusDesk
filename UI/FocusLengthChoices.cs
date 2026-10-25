using FocusDesk.Services;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The session lengths offered, and the list box that offers them. One list for both surfaces that
/// ask for a length — the Focus settings page and the status window's start box — so the two can
/// never offer different choices.
/// </summary>
internal static class FocusLengthChoices
{
    /// <summary>The lengths offered, in minutes, in the order they are offered.</summary>
    private static readonly int[] Presets = [15, 25, 45, 60, 90, 120, 180, 240];

    /// <summary>A length as the list shows it: whole hours from one hour up, minutes otherwise.</summary>
    private static string Label(int minutes) => minutes switch
    {
        60                          => AppText.Get("FocusLengthOneHour"),
        > 60 when minutes % 60 == 0 => AppText.Format("FocusLengthHours", minutes / 60),
        _                           => AppText.Format("FocusLengthMinutes", minutes),
    };

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
        if (!offered.Contains(stored))
        {
            int at = offered.FindIndex(p => p > stored);
            offered.Insert(at < 0 ? offered.Count : at, stored);
        }

        foreach (int value in offered)
            combo.Items.Add(new ComboBoxItem { Content = Label(value), Tag = value });

        combo.SelectedIndex = offered.IndexOf(stored);
    }

    /// <summary>The length a list is showing, or null where nothing is selected.</summary>
    public static int? Selected(ComboBox combo)
    {
        ArgumentNullException.ThrowIfNull(combo);
        return combo.SelectedItem is ComboBoxItem { Tag: int minutes } ? minutes : null;
    }
}
