using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The Automatic focus session page: one row opening the setup guide for starting a session from a
/// Home Assistant automation at a set time.
/// </summary>
public sealed partial class AutomaticSessionSettingsPanel : UserControl
{
    public AutomaticSessionSettingsPanel()
    {
        InitializeComponent();

        HeadingText.Text        = AppText.Get("AutomaticSessionHeading");
        GuideCard.Header        = AppText.Get("AutomaticSessionGuideHeader");
        GuideCard.Description   = AppText.Get("AutomaticSessionGuideDescription");
        OpenGuideButton.Content = AppText.Get("AutomaticSessionGuideButton");
    }

    private void OnOpenGuideClick(object sender, RoutedEventArgs e) => SetupGuideWindow.Open();
}
