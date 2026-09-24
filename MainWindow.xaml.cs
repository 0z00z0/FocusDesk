using FocusDesk.Services;
using FocusDesk.UI;
using Microsoft.UI.Xaml;

namespace FocusDesk;

/// <summary>
/// The window that stands in for the tray icon: it opens the Settings window and it is what closing
/// ends the process by. The tray menu replaces it.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
            // Closing this window ends the process while there is no tray icon, so the session's
            // own shutdown runs here. The session record stays on disk; the next start resumes it.
            FocusSessionService.Stop();
    }

    private void OnSettings(object sender, RoutedEventArgs e) => SettingsShellHost.Open();
}
