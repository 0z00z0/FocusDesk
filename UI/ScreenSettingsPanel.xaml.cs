using CommunityToolkit.WinUI.Controls;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace FocusDesk.UI;

/// <summary>
/// The Screen break page: whether a screen break dims the screen, blocks the mouse and keyboard and
/// blocks the network, the display's brightness, and a button that puts back the level in force before the first change.
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

    private readonly List<Button> _removeButtons = [];
    private int _wanted;
    private bool _updating;

    public ScreenSettingsPanel()
    {
        InitializeComponent();
        PageTitle.Text = AppText.Get("FocusScreenBreakHeading");
        DuringHeader.Heading = AppText.Get("ScreenBreakDuringHeading");
        DimsScreenCard.Header = AppText.Get("ScreenDimsHeader");
        DimsScreenCard.Description = AppText.Get("ScreenDimsDescription");
        DimsScreenInfo.Subject = AppText.Get("ScreenDimsHeader");
        DimsScreenInfo.Info = AppText.Get("ScreenDimsInfo");
        BlocksInputCard.Header = AppText.Get("ScreenBlocksInputHeader");
        BlocksInputCard.Description = AppText.Get("ScreenBlocksInputDescription");
        BlocksInputInfo.Subject = AppText.Get("ScreenBlocksInputHeader");
        BlocksInputInfo.Info = AppText.Get("ScreenBlocksInputInfo");
        BlocksNetworkCard.Header = AppText.Get("ScreenBlocksNetworkHeader");
        BlocksNetworkCard.Description = AppText.Get("ScreenBlocksNetworkDescription");
        BlocksNetworkInfo.Subject = AppText.Get("ScreenBlocksNetworkHeader");
        BlocksNetworkInfo.Info = AppText.Get("ScreenBlocksNetworkInfo");
        BrightnessHeader.Heading = AppText.Get("ScreenBrightnessHeading");
        ScreenUnsupportedCard.Header = AppText.Get("ScreenBrightnessHeading");
        ScreenUnsupportedCard.Description = AppText.Get("ScreenUnsupportedDescription");
        ScreenBrightnessCard.Header = AppText.Get("ScreenBrightnessHeading");
        ScreenBrightnessCard.Description = AppText.Get("ScreenBrightnessDescription");
        BrightnessInfo.Subject = AppText.Get("ScreenBrightnessHeading");
        BrightnessInfo.Info = AppText.Get("ScreenBrightnessInfo");
        ScreenRestoreCard.Header = AppText.Get("ScreenRestoreHeader");
        ScreenRestoreCard.Description = AppText.Get("ScreenRestoreDescription");
        ScreenRestoreBtn.Content = AppText.Get("ScreenRestoreButton");
        ExceptionsCaption.Text = AppText.Get("ScreenExceptionsCaption");
        ExceptionAddBtn.Content = AppText.Get("FocusProgramsAddButton");
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

            // A running session reports the levers it holds; with none running, the next one's defaults.
            var session = FocusSessionService.Current;
            var (dims, input, network) = SettingsService.Read(
                s => (s.FocusDimsScreen, s.FocusBlocksInput, s.FocusBlocksNetwork));
            FocusDimsScreenToggle.IsOn    = session.IsRunning ? session.DimsScreen    : dims;
            FocusBlocksInputToggle.IsOn   = session.IsRunning ? session.BlocksInput   : input;
            FocusBlocksNetworkToggle.IsOn = session.IsRunning ? session.BlocksNetwork : network;

            ExceptionsPanel.Visibility = FocusBlocksNetworkToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
            BuildExceptionRows();

            ApplySessionLock();
        }
        finally { _updating = false; }
    }

    /// <summary>Greys the slider and Restore for the length of a session: both write the brightness
    /// record the session's dim holds, so either would lift the dim.</summary>
    public void ApplySessionLock()
    {
        bool locked = FocusSessionService.LeversAreLocked;
        FocusDimsScreenToggle.IsEnabled  = !locked;
        FocusBlocksInputToggle.IsEnabled = !locked;
        FocusBlocksNetworkToggle.IsEnabled = !locked;
        ExceptionAddBtn.IsEnabled = !locked;
        foreach (var remove in _removeButtons) remove.IsEnabled = !locked;
        ScreenBrightnessSlider.IsEnabled = !locked;
        ScreenRestoreBtn.IsEnabled = !locked && ScreenBrightnessService.Holding;
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
        // A drag that ended just before a session started must not lift its dim.
        if (FocusSessionService.LeversAreLocked) return;
        ScreenBrightnessService.Set(_wanted, ActionCause.SettingsPage("Screen break"));
        ApplySessionLock();
    }

    private void OnScreenRestore(object sender, RoutedEventArgs e)
    {
        _debounce.Stop();
        ScreenBrightnessService.Restore(ActionCause.SettingsPage("Screen break"));
        Reload();
    }

    private void OnFocusDimsScreenToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        SettingsService.Update(s => s.FocusDimsScreen = FocusDimsScreenToggle.IsOn);
    }

    private void OnFocusBlocksInputToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        SettingsService.Update(s => s.FocusBlocksInput = FocusBlocksInputToggle.IsOn);
    }

    private void OnFocusBlocksNetworkToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        SettingsService.Update(s => s.FocusBlocksNetwork = FocusBlocksNetworkToggle.IsOn);
        ExceptionsPanel.Visibility = FocusBlocksNetworkToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One card per program that keeps the network, with its icon, name and Remove.</summary>
    private void BuildExceptionRows()
    {
        ExceptionRows.Children.Clear();
        _removeButtons.Clear();
        foreach (var exception in SettingsService.Read(s => s.ScreenBreakNetworkExceptions.ToList()))
        {
            var entry = new FocusProgramEntry { Kind = exception.Kind, Id = exception.Id, StartEntry = exception.StartEntry };
            var remove = ProgramRowParts.RemoveButton();
            remove.Click += (_, _) => RemoveException(exception);
            remove.IsEnabled = !FocusSessionService.LeversAreLocked;
            _removeButtons.Add(remove);

            ExceptionRows.Children.Add(ProgramRowParts.Card(entry, remove));
        }
    }

    private void RemoveException(NetworkException exception)
    {
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        SettingsService.Update(s => s.ScreenBreakNetworkExceptions.RemoveAll(
            e => e.Kind == exception.Kind && FocusAllowedPrograms.Same(e.Id, exception.Id)));
        BuildExceptionRows();
    }

    private void OnAddException(object sender, RoutedEventArgs e)
    {
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        try
        {
            var listed = SettingsService.Read(s => s.ScreenBreakNetworkExceptions.Select(x => (x.Kind, x.Id)).ToList());
            // Program files only: a web app shares its browser's file, and a Store package has no firewall rule yet.
            new ProgramPickerWindow(chosen => DispatcherQueue.TryEnqueue(() => AddException(chosen)),
                                    listed, [FocusProgramKind.ProgramFile])
                .Activate();
        }
        catch (Exception ex) { AppLog.Error("ScreenSettingsPanel.OnAddException", ex); }
    }

    private void AddException(ProgramChoice chosen)
    {
        if (FocusSessionService.LeversAreLocked) { Reload(); return; }
        string id = FocusAllowedPrograms.NormaliseId(chosen.Kind, chosen.Id);
        if (chosen.Kind != FocusProgramKind.ProgramFile || id.Length == 0) return;

        SettingsService.Update(s =>
        {
            if (s.ScreenBreakNetworkExceptions.Any(x => x.Kind == chosen.Kind && FocusAllowedPrograms.Same(x.Id, id))) return;
            s.ScreenBreakNetworkExceptions.Add(new NetworkException { Kind = chosen.Kind, Id = id, StartEntry = chosen.StartEntry });
        });
        BuildExceptionRows();
    }
}
