using System.ComponentModel;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>One row of the picker: the choice, and its icon once the shell has drawn it.</summary>
internal sealed partial class ProgramPickerRow(ProgramChoice choice) : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public ProgramChoice Choice { get; } = choice;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    /// <summary>Whether its icon has been asked for, so a row scrolled past twice asks once.</summary>
    public bool IconRequested { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Picks a program off the Start menu's own list, Store apps and web apps included, narrowed by
/// typing. There is no file dialog and no browsing for program files.
/// </summary>
/// <remarks>
/// <para>The list is built afresh on every open, off a thread of its own, so a program installed
/// since the last open is on it and the window draws while it is read.</para>
/// <para>Always on top of the Settings window it was opened from; Escape or Cancel closes it.</para>
/// </remarks>
internal sealed partial class ProgramPickerWindow : Window
{
    private const int WidthDip  = 460;
    private const int HeightDip = 520;

    private readonly Action<ProgramChoice> _onChosen;

    private IReadOnlyList<ProgramPickerRow> _all = [];
    private bool _placed;
    private bool _closing;

    /// <param name="onChosen">Handed the chosen row. Only its identifiers reach the list: the name on
    /// it is drawn and discarded.</param>
    internal ProgramPickerWindow(Action<ProgramChoice> onChosen)
    {
        InitializeComponent();
        _onChosen = onChosen;

        Title = AppText.Get("PickerTitle");
        HeadingText.Text = Title;
        QueryBox.PlaceholderText = AppText.Get("PickerSearchPlaceholder");
        AutomationProperties.SetName(QueryBox, AppText.Get("PickerSearchName"));
        AutomationProperties.SetName(ProgramList, AppText.Get("PickerListName"));
        LoadingText.Text = AppText.Get("PickerLoading");
        NoteText.Text = AppText.Get("PickerNote");
        CancelButton.Content = AppText.Get("PickerCancel");
        AddButton.Content = AppText.Get("PickerAdd");

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppTitleBar.Apply(this);
        AppBackdrop.ApplyTo(this);

        Activated += OnActivated;
        Closed    += (_, _) => _closing = true;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var built = await ProgramCatalogue.BuildAsync();
        if (_closing) return;

        _all = [.. built.Select(c => new ProgramPickerRow(c))];
        LoadingPanel.Visibility = Visibility.Collapsed;
        Show();
    }

    private void Show()
    {
        var matching = ProgramCatalogue.Match([.. _all.Select(r => r.Choice)], QueryBox.Text);
        var rows = _all.Where(r => matching.Contains(r.Choice)).ToList();

        ProgramList.ItemsSource = rows;
        ProgramList.Visibility  = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility    = rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyText.Text = AppText.Get(_all.Count == 0 ? "PickerNothingFound" : "PickerNothingMatches");

        if (rows.Count > 0) ProgramList.SelectedIndex = 0;
        AddButton.IsEnabled = rows.Count > 0;
    }

    /// <summary>Asks for a row's icon as it is about to be drawn, so a long list reads only the
    /// icons somebody scrolls to.</summary>
    private void OnRowComingIntoView(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not ProgramPickerRow row || row.IconRequested) return;
        row.IconRequested = true;
        ProgramIconLoader.Request(row.Choice.StartEntry ?? row.Choice.Id, image => row.Icon = image);
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (LoadingPanel.Visibility == Visibility.Visible) return;
        Show();
    }

    /// <summary>Typing then pressing Enter takes the top row, so the whole choice is made without
    /// leaving the keyboard.</summary>
    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        Choose();
    }

    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = ProgramList.SelectedItem is ProgramPickerRow;

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Choose();

    private void OnAddButton(object sender, RoutedEventArgs e) => Choose();

    /// <summary>Hands back the selected row and closes.</summary>
    private void Choose()
    {
        if (ProgramList.SelectedItem is not ProgramPickerRow chosen) return;

        _onChosen(chosen.Choice);
        Dismiss();
    }

    private void OnCancelButton(object sender, RoutedEventArgs e) => Dismiss();

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Dismiss();
    }

    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        try { Close(); }
        catch (Exception ex) { AppLog.Error("ProgramPickerWindow.Dismiss", ex); }
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
            QueryBox.Focus(FocusState.Programmatic);
        }
        catch (Exception ex) { AppLog.Error("ProgramPickerWindow.Place", ex); }
    }
}
