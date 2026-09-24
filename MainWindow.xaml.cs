using FocusDesk.Services;
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
        Closed += (_, _) =>
        {
            // A slider position the debounce is still holding would otherwise be lost with the window.
            ScreenPanel.Flush();
            // Closing this window ends the process while there is no tray icon, so the session's
            // own shutdown runs here. The session record stays on disk; the next start resumes it.
            FocusSessionService.Stop();
        };
    }
}
