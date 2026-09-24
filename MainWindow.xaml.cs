using Microsoft.UI.Xaml;

namespace FocusDesk;

/// <summary>
/// The one window there is. It hosts whichever page has been built, and is replaced by the Settings
/// shell and the tray.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // A slider position the debounce is still holding would otherwise be lost with the window.
        Closed += (_, _) => ScreenPanel.Flush();
    }
}
