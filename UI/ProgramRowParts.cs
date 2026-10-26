using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FocusDesk.UI;

/// <summary>The parts a program row is built from, shared by the Program focus page and the Screen
/// break exception list so both rows look alike.</summary>
internal static class ProgramRowParts
{
    /// <summary>One program's card: its icon, its name, and beneath it the path or package in a
    /// smaller type, so the line wraps as little as the width allows.</summary>
    public static SettingsCard Card(FocusProgramEntry entry, object content)
    {
        var icon = new ImageIcon { Width = 24, Height = 24 };
        ProgramIconLoader.Request(entry.StartEntry ?? entry.Id, image => icon.Source = image);

        return new SettingsCard
        {
            HeaderIcon  = icon,
            Header      = ProgramNames.For(entry),
            Description = new TextBlock
            {
                Text = ProgramCatalogue.IsPresent(entry)
                    ? FocusAllowedPrograms.Describe(entry)
                    : AppText.Get("FocusProgramNotFound"),
                FontSize     = 11,
                TextWrapping = TextWrapping.Wrap,
            },
            Content     = content,
        };
    }

    /// <summary>Remove as a trash-can button: "Remove" on hover and to a screen reader. The artwork
    /// keeps its colours when the button greys, so it fades instead.</summary>
    public static Button RemoveButton()
    {
        var button = new Button
        {
            Content = new ImageIcon
            {
                Width  = 16,
                Height = 16,
                Source = new SvgImageSource(new Uri("ms-appx:///Assets/trash.svg")),
            },
            Padding = new Thickness(8, 6, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        string remove = AppText.Get("FocusProgramRemove");
        AutomationProperties.SetName(button, remove);
        ToolTipService.SetToolTip(button, remove);
        button.IsEnabledChanged += (_, _) => Fade(button);
        return button;
    }

    private static void Fade(Button button)
    {
        if (button.Content is UIElement art) art.Opacity = button.IsEnabled ? 1 : 0.4;
    }
}
