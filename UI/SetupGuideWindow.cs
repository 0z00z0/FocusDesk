using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>
/// The setup guide for an automatic focus session, shown in the embedded browser in the interface
/// language. One window at a time: a second request brings the open one forward.
/// </summary>
/// <remarks>Shown here rather than handed to the default browser: FocusDesk runs elevated, so a
/// browser it starts would run elevated too, and a local file opened through the shell loses the
/// language fragment.</remarks>
internal sealed partial class SetupGuideWindow : Window
{
    private const int WidthDip  = 860;
    private const int HeightDip = 900;

    private static SetupGuideWindow? _open;

    private readonly WebView2 _view;
    private readonly Grid _root;
    private bool _placed;

    /// <summary>Opens the guide, or brings the open one forward.</summary>
    internal static void Open()
    {
        try
        {
            _open ??= new SetupGuideWindow();
            _open.Activate();
        }
        catch (Exception ex) { AppLog.Error("SetupGuideWindow.Open", ex); }
    }

    private SetupGuideWindow()
    {
        Title = AppText.Get("SetupGuideWindowTitle");

        // The page's own ground, so nothing flashes white before it draws.
        var ground = AppColors.FromPacked(AppPalette.GroundTint(isDark: true));
        _view = new WebView2 { DefaultBackgroundColor = ground };
        _root = new Grid { Background = new SolidColorBrush(ground) };
        _root.Children.Add(_view);
        Content = _root;

        AppTitleBar.Apply(this);
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _open = null;
            _view.Close();
        };

        Load();
    }

    // async void: started from the constructor, which cannot wait. Every path is caught.
    private async void Load()
    {
        try
        {
            await _view.EnsureCoreWebView2Async(await EmbeddedBrowser.Environment);

            var core = _view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping(
                SetupGuidePage.Host, AppContext.BaseDirectory, CoreWebView2HostResourceAccessKind.Allow);

            core.Settings.AreDevToolsEnabled            = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled            = false;

            // The guide links nowhere; anything else asking to load is not shown.
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) =>
            {
                if (!SetupGuidePage.IsGuide(args.Uri)) args.Cancel = true;
            };
            core.NavigationCompleted += (_, args) =>
            {
                if (args.IsSuccess) return;
                var status = args.WebErrorStatus;
                DispatcherQueue.TryEnqueue(() => ShowFailure($"navigation failed: {status}"));
            };

            _view.Source = SetupGuidePage.Address(AppText.Get("SetupGuideLanguage"));
        }
        catch (Exception ex)
        {
            AppLog.Error("SetupGuideWindow.Load", ex);
            ShowFailure("the embedded browser did not start");
        }
    }

    /// <summary>Replaces the browser with one line saying the guide could not be shown, rather than
    /// leaving an empty window or the browser's own light error page.</summary>
    private void ShowFailure(string why)
    {
        try
        {
            AppLog.Error($"SetupGuideWindow: {why}", null);
            _root.Children.Clear();
            _root.Children.Add(new TextBlock
            {
                Text                = AppText.Get("SetupGuideFailed"),
                Margin              = new Thickness(24),
                TextWrapping        = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            });
        }
        catch (Exception ex) { AppLog.Error("SetupGuideWindow.ShowFailure", ex); }
    }

    /// <summary>Centred on the monitor under the pointer the first time it is shown, which is the
    /// monitor the Settings window's button was pressed on.</summary>
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_placed || e.WindowActivationState == WindowActivationState.Deactivated) return;
        _placed = true;

        try
        {
            var (work, scale) = MonitorMetrics.ForCursor();
            int width  = Math.Min((int)Math.Round(WidthDip * scale), work.Right - work.Left);
            int height = Math.Min((int)Math.Round(HeightDip * scale), work.Bottom - work.Top);
            AppWindow.MoveAndResize(new RectInt32(
                work.Left + (work.Right - work.Left - width) / 2,
                work.Top + (work.Bottom - work.Top - height) / 2,
                width, height));
        }
        catch (Exception ex) { AppLog.Error("SetupGuideWindow.Place", ex); }
    }
}
