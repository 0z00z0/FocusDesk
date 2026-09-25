using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Tray;

namespace FocusDesk.UI;

/// <summary>
/// The Appearance page: one row, asking the shell to keep the notification-area icon beside the
/// clock rather than in the overflow flyout.
/// </summary>
/// <remarks>The row is the stored wish. The shell acts on a placement only at the next sign-in, so
/// the toggle reflects what was asked for rather than where the icon sits now.</remarks>
public sealed partial class AppearanceSettingsPanel : UserControl
{
    private bool _updating;

    public AppearanceSettingsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    /// <summary>Brings the row into line with the stored wish.</summary>
    public void Reload()
    {
        _updating = true;
        try { PromoteToggle.IsOn = SettingsService.Current.PromoteTrayIcon; }
        finally { _updating = false; }
    }

    private void OnPromoteToggled(object sender, RoutedEventArgs e)
    {
        if (_updating) return;

        bool wanted = PromoteToggle.IsOn;
        var held = RecordIn(SettingsService.Current);

        // The wish is kept even with no icon to write for; the next start applies it.
        var record = wanted
            ? TrayIconPlacementRow.TurnOn(TrayIconHost.Placement, held)
            : TrayIconPlacementRow.TurnOff(TrayIconHost.Placement, held);

        SettingsService.Update(s =>
        {
            s.PromoteTrayIcon = wanted;
            Store(s, record);
        });
    }

    /// <summary>Asks again for the notification area where the row is on. At startup, after the
    /// icon is registered: the shell's entry for it exists only once it has been seen, and a start
    /// at sign-in is what makes the placement take effect.</summary>
    internal static void ApplyAtStartup(ITrayPlacement tray)
    {
        ArgumentNullException.ThrowIfNull(tray);

        var settings = SettingsService.Current;
        if (!settings.PromoteTrayIcon) return;

        var held = RecordIn(settings);
        var record = TrayIconPlacementRow.TurnOn(tray, held);
        if (record != held) SettingsService.Update(s => Store(s, record));
    }

    /// <summary>The restore record one settings object carries, or null where it carries none. Both
    /// halves are needed: a record naming no icon describes nothing to put back.</summary>
    /// <remarks>The stored value is the shell's own spelling: true for the notification area, and
    /// false or nothing for the overflow, which the shell reads alike.</remarks>
    internal static TrayPlacementRecord? RecordIn(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.TrayIconPromotionRestoreFor is not { Length: > 0 } text ||
            !Guid.TryParse(text, out var icon))
            return null;

        return new TrayPlacementRecord(icon, s.TrayIconPromotionRestoreValue == true
            ? TrayIconPlacement.NotificationArea
            : TrayIconPlacement.Overflow);
    }

    /// <summary>Puts a restore record into one settings object, or clears it. Separated from the
    /// handler so the round trip through the document can be exercised without a display.</summary>
    internal static void Store(AppSettings s, TrayPlacementRecord? record)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.TrayIconPromotionRestoreFor   = record?.Icon.ToString("B").ToUpperInvariant();
        s.TrayIconPromotionRestoreValue = record is { } r
            ? r.Before == TrayIconPlacement.NotificationArea
            : null;
    }
}
