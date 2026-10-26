using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>Shows one start's programs as they open, over the see-through cover, then "Completed".
/// Closing it closes the cover too; the run carries on unseen.</summary>
internal sealed partial class StartingWindow : Window
{
    private const int WidthDip = 560;
    private const int ChromeDip = 200;
    private const int RowDip = 24;
    private const int MostHeightDip = 640;

    // A window created topmost after this one sits above it for at most this long.
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static DispatcherQueue? _ui;
    private static StartingWindow? _open;

    private readonly ProgramStartRun _run;
    private readonly TextBlock[] _names;
    private readonly TextBlock[] _states;
    private readonly DispatcherQueueTimer _tick;
    private readonly IntPtr _hwnd;
    private bool _closed;

    /// <summary>Hands the window the UI thread; a start can come from MQTT's thread.</summary>
    internal static void Start(DispatcherQueue ui) => _ui = ui;

    /// <summary>Shows the run as it stands, a finished one straight as "Completed". A starting window
    /// already open gives way to it.</summary>
    internal static void Show(ProgramStartRun run)
    {
        if (_ui is not { } ui) return;
        ui.TryEnqueue(() =>
        {
            try
            {
                _open?.Dismiss();
                _open = new StartingWindow(run);
                _open.Open();
            }
            catch (Exception ex) { AppLog.Error("StartingWindow.Show", ex); }
        });
    }

    private StartingWindow(ProgramStartRun run)
    {
        InitializeComponent();
        _run = run;

        Title = "FocusDesk";
        CloseButton.Content = AppText.Get("StartingClose");
        Progress.Maximum = run.Programs.Count;

        _names = new TextBlock[run.Programs.Count];
        _states = new TextBlock[run.Programs.Count];
        for (int i = 0; i < run.Programs.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _names[i] = new TextBlock { Text = ProgramNames.For(run.Programs[i]), FontSize = 13,
                                        TextTrimming = TextTrimming.CharacterEllipsis };
            // A desktop note is long; it wraps rather than crowding out the program's name.
            _states[i] = new TextBlock { FontSize = 13, MaxWidth = 300, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(_states[i], 1);
            row.Children.Add(_names[i]);
            row.Children.Add(_states[i]);
            ProgramList.Children.Add(row);
        }

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppTitleBar.Apply(this);
        AppBackdrop.ApplyTo(this);
        _hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);

        _tick = DispatcherQueue.CreateTimer();
        _tick.Interval = TickInterval;
        _tick.Tick += (_, _) => KeepAbove();

        Closed += OnClosed;
        _run.Changed += OnRunChanged;
        Refresh();
    }

    /// <summary>The cover first, so the window is created above it.</summary>
    private void Open()
    {
        Place();
        StartingCover.Show();
        Activate();
        KeepAbove();
        _tick.Start();
        VirtualDesktops.Travels(_hwnd);
    }

    private void KeepAbove()
    {
        StartingCover.KeepUp();
        NativeMethods.RaiseToTopmost(_hwnd);
    }

    private void OnRunChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_closed) Refresh();
    });

    private void Refresh()
    {
        var results = _run.Results();
        var notes = _run.Notes();
        int tried = results.Count(r => r is not null);
        int failed = results.Count(r => r is { } result && ProgramStartRun.Failed(result));

        Progress.Value = tried;
        for (int i = 0; i < results.Length; i++)
        {
            _states[i].Text = notes[i] is { } note
                ? AppText.Format(note.Miss == DesktopMiss.DoesNotExist ? "StartingDesktopMissing" : "StartingDesktopNotReached",
                                 note.Desktop)
                : AppText.Get(StateKey(results[i]));
            bool failedHere = results[i] is { } outcome && ProgramStartRun.Failed(outcome);
            _names[i].Foreground = Themed(results[i] is null ? "TextFillColorTertiaryBrush" : "TextFillColorPrimaryBrush");
            _states[i].Foreground = Themed(failedHere ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
        }

        bool complete = tried == results.Length;
        MessageText.Inlines.Clear();
        if (complete)
        {
            MessageText.Inlines.Add(new Run { Text = AppText.Get("StartingCompleted"), FontWeight = FontWeights.SemiBold });
            MessageText.Inlines.Add(new Run { Text = " " + (failed switch
            {
                0 => AppText.Get("StartingAllStarted"),
                1 => AppText.Get("StartingOneFailed"),
                _ => AppText.Format("StartingManyFailed", failed),
            }) });
        }
        else MessageText.Inlines.Add(new Run { Text = AppText.Get("StartingWorking") });
        CloseButton.Visibility = complete ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string StateKey(ProgramStartResult? result) => result switch
    {
        null => "StartingWaiting",
        ProgramStartResult.Started => "StartingStarted",
        ProgramStartResult.AlreadyOpen => "StartingAlreadyOpen",
        ProgramStartResult.NotInStartMenu => "StartingNotInStartMenu",
        ProgramStartResult.FileMissing => "StartingFileMissing",
        _ => "StartingDidNotStart",
    };

    private static Brush? Themed(string key) =>
        Application.Current.Resources.TryGetValue(key, out var found) ? found as Brush : null;

    private void OnCloseButton(object sender, RoutedEventArgs e) => Dismiss();

    private void Dismiss()
    {
        if (_closed) return;
        try { Close(); }
        catch (Exception ex) { AppLog.Error("StartingWindow.Dismiss", ex); }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _tick.Stop();
        _run.Changed -= OnRunChanged;
        VirtualDesktops.Stays(_hwnd);

        // A window replaced by a newer start leaves the newer one's cover alone.
        if (_open != this) return;
        _open = null;
        StartingCover.Hide();
        AppLog.Info("Focus: the starting window and its cover are closed.");
    }

    /// <summary>Centred on the display under the pointer, tall enough for the list up to a limit.</summary>
    private void Place()
    {
        try
        {
            var (work, scale) = MonitorMetrics.ForCursor();
            int heightDip = Math.Min(ChromeDip + RowDip * _run.Programs.Count, MostHeightDip);
            int width  = Math.Min((int)Math.Round(WidthDip * scale), work.Right - work.Left);
            int height = Math.Min((int)Math.Round(heightDip * scale), work.Bottom - work.Top);
            AppWindow.MoveAndResize(new RectInt32(
                work.Left + (work.Right - work.Left - width) / 2,
                work.Top + (work.Bottom - work.Top - height) / 2,
                width, height));
        }
        catch (Exception ex) { AppLog.Error("StartingWindow.Place", ex); }
    }
}
