using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FocusDesk.UI;

/// <summary>
/// The Screen page: what the display is on, a slider that sets it, and a button that puts back the
/// level in force before the first change.
/// </summary>
/// <remarks>The level moves outside this application — Windows itself moves it without this
/// application hearing anything — so <see cref="Reload"/> reads it afresh every time the page is
/// shown.</remarks>
public sealed partial class ScreenSettingsPanel : UserControl
{
    /// <summary>Holds the slider's last position back from the display. A brightness write is a
    /// synchronous WMI call, and one per pixel of drag would stall the thread that draws the
    /// slider.</summary>
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private int _wanted;
    private bool _updating;

    public ScreenSettingsPanel()
    {
        InitializeComponent();
        _debounce.Tick += (_, _) => Apply();
        Loaded += (_, _) => Reload();
    }

    /// <summary>Reads the display and brings every control into line with it.</summary>
    public void Reload()
    {
        bool supported = ScreenBrightnessService.IsSupported;
        int? level = supported ? ScreenBrightnessService.Current : null;

        _updating = true;
        try
        {
            ScreenUnsupportedCard.Visibility = supported ? Visibility.Collapsed : Visibility.Visible;
            ScreenBrightnessCard.Visibility  = supported ? Visibility.Visible : Visibility.Collapsed;
            ScreenRestoreCard.Visibility     = supported ? Visibility.Visible : Visibility.Collapsed;

            if (level is { } percent)
            {
                ScreenBrightnessSlider.Value = percent;
                ScreenBrightnessValue.Text   = $"{percent} %";
            }

            ScreenRestoreBtn.IsEnabled = ScreenBrightnessService.Holding;
        }
        finally { _updating = false; }
    }

    /// <summary>Writes a position the debounce is still holding. Called before the page goes away,
    /// so a drag that ended a moment earlier is not lost.</summary>
    public void Flush()
    {
        if (_debounce.IsEnabled) Apply();
    }

    private void OnScreenBrightnessChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating) return;

        _wanted = (int)Math.Round(e.NewValue);
        ScreenBrightnessValue.Text = $"{_wanted} %";

        _debounce.Stop();
        _debounce.Start();
    }

    private void Apply()
    {
        _debounce.Stop();
        ScreenBrightnessService.Set(_wanted, ActionCause.SettingsPage("Screen"));
        ScreenRestoreBtn.IsEnabled = ScreenBrightnessService.Holding;
    }

    private void OnScreenRestore(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        ScreenBrightnessService.Restore(ActionCause.SettingsPage("Screen"));
        Reload();
    }
}
