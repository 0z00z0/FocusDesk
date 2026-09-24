using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FocusDesk;

/// <summary>
/// The host window: one pixel, off screen, never activated and never in the switcher. It exists so
/// the XAML runtime has a window to own while the only thing on screen is the notification-area
/// icon.
/// </summary>
/// <remarks>Closing it does not end the process — <c>App</c> sets
/// <see cref="DispatcherShutdownMode.OnExplicitShutdown"/>, so only leaving from the tray menu
/// does.</remarks>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        AppWindow.IsShownInSwitchers = false;

        // Chrome removed as well as the window moved away, so nothing is visible even in the moment
        // between creation and the move.
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        presenter.IsResizable   = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(1, 1));
        AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
    }
}
