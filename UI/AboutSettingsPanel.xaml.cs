using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Brand.WinUI;
using ZeroZero.Update.Win32;

namespace FocusDesk.UI;

/// <summary>
/// The About page: the shared About control inside a card, and the Check for updates button under
/// it. The button joins the one check every surface shares, so it shows a check the
/// notification-area menu started as readily as one it started itself.
/// </summary>
/// <remarks>The coordinator lives for the process, so <see cref="Detach"/> must run when the window
/// closes or a torn-down page stays reachable and keeps being told about checks.</remarks>
public sealed partial class AboutSettingsPanel : UserControl
{
    // The check the button currently reflects. A newer one replaces it, and a stale result is dropped.
    private Task<UpdateFlowRun>? _following;

    // The run behind the update-available state, offered as it stands when the button is selected.
    private UpdateFlowRun? _available;

    private bool _detached;

    public AboutSettingsPanel()
    {
        InitializeComponent();

        About.SetInfo(AboutContent.Build());
        About.Width = AboutContent.ContentWidthDip;
        FrameHeader();

        Show(UpdateButtonPolicy.Rest);
        CheckForUpdatesButton.Click += (_, _) => OnClick();

        // The control returns from Success to Rest by itself and leaves the last label in place.
        CheckForUpdatesButton.RegisterPropertyChangedCallback(
            BrandBracketButton.StateProperty, OnStateChanged);

        if (AppUpdates.Checks is { } checks) checks.CheckStarted += Follow;
    }

    /// <summary>Stops following the shared check and drops any fetch the About control has in
    /// flight. From the window's <c>Closed</c> and nowhere else.</summary>
    public void Detach()
    {
        _detached = true;
        if (AppUpdates.Checks is { } checks) checks.CheckStarted -= Follow;
        About.CancelPendingFetch();
    }

    /// <summary>Frames the control's coloured header block. The control offers no frame of its own
    /// and names no part of itself: the block is the first child of its root panel. A later layout
    /// without that shape is left unframed rather than failing.</summary>
    private void FrameHeader()
    {
        if (About.Content is Panel { Children.Count: > 0 } root && root.Children[0] is Border header)
            header.Style = (Style)Resources["AboutHeaderFrameStyle"];
    }

    private void OnClick()
    {
        try
        {
            switch (CheckForUpdatesButton.State)
            {
                case BrandBracketButtonState.Busy:
                    return;

                case BrandBracketButtonState.Attention when _available is { Release: { } release }:
                    _available = null;
                    Show(UpdateButtonPolicy.Rest);
                    _ = AppUpdates.InstallAsync(release);
                    return;

                default:
                    if (AppUpdates.Checks is { } checks) Follow(checks.Run());
                    return;
            }
        }
        catch (Exception ex) { AppLog.Error("AboutSettingsPanel.OnClick", ex); }
    }

    private void OnStateChanged(DependencyObject sender, DependencyProperty property)
    {
        if (CheckForUpdatesButton.State == BrandBracketButtonState.Rest &&
            CheckForUpdatesButton.Label != UpdateButtonPolicy.RestLabel)
            CheckForUpdatesButton.Label = UpdateButtonPolicy.RestLabel;
    }

    // async void: an event and click continuation with nothing to return to. Every path is caught.
    // The coordinator raises CheckStarted from whichever thread called Run, so this body can already
    // be off the UI thread before the first await; every touch of the button goes through RunOnUi.
    private async void Follow(Task<UpdateFlowRun> check)
    {
        try
        {
            // The check this button started is announced before it is returned, so it arrives twice.
            if (_detached || ReferenceEquals(check, _following)) return;

            _following = check;
            _available = null;
            RunOnUi(() => Show(UpdateButtonPolicy.Checking));

            var run = await check;
            if (_detached || !ReferenceEquals(check, _following)) return;

            RunOnUi(() =>
            {
                _available = run.Result == UpdateFlowResult.UpdateAvailable ? run : null;
                Show(UpdateButtonPolicy.After(run));
                // Everything the button cannot say itself: a click has to end in an answer.
                if (UpdateButtonPolicy.ShowsNotice(run.Result)) _ = AppUpdates.SayAsync(run);
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("AboutSettingsPanel.Follow", ex);
            try { if (!_detached) RunOnUi(() => Show(UpdateButtonPolicy.Rest)); }
            catch (Exception inner) { AppLog.Error("AboutSettingsPanel.Follow.Rest", inner); }
        }
    }

    /// <summary>Marshals onto the button's UI thread. An unhandled exception inside a raw dispatcher
    /// callback is a stowed exception that tears the process down, so every touch reachable off the
    /// UI thread goes through here.</summary>
    private void RunOnUi(Action action)
    {
        try
        {
            CheckForUpdatesButton.DispatcherQueue?.TryEnqueue(() =>
            {
                // Detach can land between the enqueue and the callback, and the window can have been
                // destroyed in between — nothing left to update.
                if (_detached) return;
                try { action(); }
                catch (Exception ex) { AppLog.Error("AboutSettingsPanel.RunOnUi", ex); }
            });
        }
        catch (Exception ex) { AppLog.Error("AboutSettingsPanel.RunOnUi enqueue", ex); }
    }

    private void Show(UpdateButtonPolicy.Look look)
    {
        CheckForUpdatesButton.Label = look.Label;
        CheckForUpdatesButton.State = look.State;
    }
}
