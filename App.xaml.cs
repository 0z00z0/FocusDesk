using Microsoft.UI.Xaml;

namespace FocusDesk;

/// <summary>
/// The application object. Holds no behaviour yet — the tray icon, the session engine and the
/// windows arrive with the code they belong to.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
