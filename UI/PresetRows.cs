using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ZeroZero.Brand.Core;

namespace FocusDesk.UI;

/// <summary>A saved-value list: one row per item and the marking on the row in use. Copied from
/// ChargeKeeper's Smart Charge presets (commit 5727ea5), keyed on each item's key, not its name.</summary>
internal static class PresetRows
{
    /// <summary>The parts of a built row its page still drives.</summary>
    internal sealed record Parts(SettingsExpander Expander, TextBlock Header, Button Activate,
                                 TextBlock Error, Button Delete, FrameworkElement DeleteFrame);

    /// <summary>Builds the shell around <paramref name="cards"/>; <paramref name="tag"/> is the item's
    /// key, which <see cref="Section.RefreshActivation"/> matches against.</summary>
    public static Parts Build(string header, string description, object tag,
                              IList<SettingsCard> cards, Brush? errorBrush, string deleteLabel)
    {
        // A TextBlock so RefreshActivation can set its Foreground and FontWeight on the row in use.
        var headerText = new TextBlock { Text = header };

        // In the header row, so activation is one click from the list without opening the editor.
        var activate = new Button { Tag = tag, MinWidth = 88 };

        var error = new TextBlock
        {
            FontSize     = 11,
            TextWrapping = TextWrapping.Wrap,
            Visibility   = Visibility.Collapsed,
            Foreground   = errorBrush,
        };
        var delete = new Button { Content = deleteLabel };
        // A disabled button shows no hover text of its own, so the reason sits on a frame around it.
        var deleteFrame = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                                     HorizontalAlignment = HorizontalAlignment.Left };
        deleteFrame.Children.Add(delete);
        var footer = new StackPanel { Spacing = 6, Margin = new Thickness(0, 6, 0, 2) };
        footer.Children.Add(error);
        footer.Children.Add(deleteFrame);

        var expander = new SettingsExpander
        {
            Header      = headerText,
            Content     = activate,
            ItemsSource = cards,
            ItemsFooter = footer,
        };
        if (description.Length > 0) expander.Description = description;

        return new Parts(expander, headerText, activate, error, delete, deleteFrame);
    }

    /// <summary>One saved-value list. The list is never empty, so it carries no empty state.</summary>
    internal sealed class Section
    {
        public required StackPanel Panel { get; init; }

        /// <summary>How many saved values there are, read afresh on every rebuild.</summary>
        public required Func<int> Count { get; init; }

        /// <summary>One row, by position in the list.</summary>
        public required Func<int, SettingsExpander> BuildRow { get; init; }

        /// <summary>The key of the row in use, or null when none is.</summary>
        public required Func<object?> ActiveTag { get; init; }

        /// <summary>Whether a row may offer activation at all; an affordance that cannot work is
        /// hidden rather than shown.</summary>
        public Func<object, bool> OffersActivation { get; init; } = _ => true;

        public required string ActiveTip { get; init; }
        public required string IdleTip   { get; init; }

        /// <summary>Discards every row and builds the list again from the stored values.</summary>
        public void Rebuild()
        {
            Panel.Children.Clear();
            ApplyActiveResources(Panel);

            int count = Count();
            for (int i = 0; i < count; i++) Panel.Children.Add(BuildRow(i));
            RefreshActivation();
        }

        /// <summary>Moves the marking to whatever is in use now.</summary>
        public void RefreshActivation()
        {
            object? active = ActiveTag();

            foreach (var row in Panel.Children.OfType<SettingsExpander>())
            {
                if (row.Content is not Button button || button.Tag is not { } tag) continue;

                bool isActive  = active is not null && Equals(tag, active);
                bool offered   = isActive || OffersActivation(tag);
                button.Content = AppText.Get(isActive ? "NamedSessionInUse" : "NamedSessionActivate");
                button.IsEnabled  = !isActive;
                button.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
                ToolTipService.SetToolTip(button, isActive ? ActiveTip : IdleTip);

                // Colour is never the only cue: the name takes the accent and the weight too.
                if (row.Header is TextBlock header) StyleActiveName(header, isActive && offered);
            }
        }
    }

    // FocusDesk's accent is amber, and black is the legible label on it (9.5:1).
    private static SolidColorBrush AccentBrush() => new(AppColors.FromHex(Brand.ColorAmber));

    /// <summary>The in-use row disables its own button, so the disabled visual state is what shows
    /// the marker. Set before the rows are parented, or their templates never resolve them.</summary>
    private static void ApplyActiveResources(StackPanel panel)
    {
        panel.Resources["ButtonBackgroundDisabled"]  = AccentBrush();
        panel.Resources["ButtonBorderBrushDisabled"] = AccentBrush();
        panel.Resources["ButtonForegroundDisabled"]  = new SolidColorBrush(Microsoft.UI.Colors.Black);
    }

    private static void StyleActiveName(TextBlock header, bool active)
    {
        if (active)
        {
            header.Foreground = AccentBrush();
            header.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        }
        else
        {
            header.ClearValue(TextBlock.ForegroundProperty);
            header.ClearValue(TextBlock.FontWeightProperty);
        }
    }
}
