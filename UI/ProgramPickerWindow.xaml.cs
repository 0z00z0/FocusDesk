using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>
/// Picks a program off a list rather than out of a folder: everything open right now, everything the
/// Start menu holds, narrowed by typing.
/// </summary>
/// <remarks>
/// <para>The list is built afresh on every open, off a thread of its own, so a program started since
/// the last open is on it and the window draws while it is read.</para>
/// <para>The file dialog stays as a second route, for a program in neither source — a portable
/// executable in a folder no Start menu knows about.</para>
/// <para>Always on top of the Settings window it was opened from; Escape or Cancel closes it.</para>
/// </remarks>
internal sealed partial class ProgramPickerWindow : Window
{
    private const int WidthDip  = 460;
    private const int HeightDip = 520;

    private readonly Action<string> _onChosen;

    private IReadOnlyList<ProgramChoice> _all = [];
    private bool _placed;
    private bool _closing;

    /// <param name="onChosen">Handed the chosen executable's full path. Nothing else about the
    /// chosen row leaves this window: the name on it is drawn and discarded.</param>
    internal ProgramPickerWindow(Action<string> onChosen)
    {
        InitializeComponent();
        Title = "Allow a program";

        _onChosen = onChosen;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppTitleBar.Apply(this);

        Activated += OnActivated;
        Closed    += (_, _) => _closing = true;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var built = await ProgramCatalogue.BuildAsync();
        if (_closing) return;

        _all = built;
        LoadingPanel.Visibility = Visibility.Collapsed;
        Show(ProgramCatalogue.Match(_all, QueryBox.Text));
    }

    private void Show(IReadOnlyList<ProgramChoice> matching)
    {
        ProgramList.ItemsSource = matching;
        ProgramList.Visibility  = matching.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Visibility    = matching.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (matching.Count > 0) ProgramList.SelectedIndex = 0;
        AddButton.IsEnabled = matching.Count > 0;
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs e)
    {
        if (LoadingPanel.Visibility == Visibility.Visible) return;
        Show(ProgramCatalogue.Match(_all, QueryBox.Text));
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
        AddButton.IsEnabled = ProgramList.SelectedItem is ProgramChoice;

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => Choose();

    private void OnAddButton(object sender, RoutedEventArgs e) => Choose();

    /// <summary>Hands back the selected row's path and closes. The path is what is stored; the name
    /// beside it is neither returned nor written anywhere.</summary>
    private void Choose()
    {
        if (ProgramList.SelectedItem is not ProgramChoice chosen) return;

        _onChosen(chosen.Path);
        Dismiss();
    }

    private void OnBrowseButton(object sender, RoutedEventArgs e)
    {
        var owner = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        if (ProgramFileDialog.Choose(owner, "Allow a program through a focus session") is not { } file)
            return;

        _onChosen(file);
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
