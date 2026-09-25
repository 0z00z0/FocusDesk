using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusDesk.UI;

/// <summary>
/// The Appearance page: one row, asking the shell to keep the notification-area icon beside the
/// clock rather than in the overflow flyout.
/// </summary>
/// <remarks>The row is the stored wish. Whether the shell honours it is not readable back with any
/// certainty, so the toggle reflects what was asked for rather than what happened.</remarks>
public sealed partial class AppearanceSettingsPanel : UserControl
{
    private readonly ITrayPromotionStore _store = new RegistryTrayPromotionStore();

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
        var icon = TrayIconIdentity.Value;
        var held = RecordIn(SettingsService.Current);

        var record = wanted
            ? TrayIconPromotion.TurnOn(_store, icon, held)
            : TrayIconPromotion.TurnOff(_store, icon, held);

        SettingsService.Update(s =>
        {
            s.PromoteTrayIcon = wanted;
            Store(s, record);
        });
    }

    /// <summary>The restore record one settings object carries, or null where it carries none. Both
    /// halves are needed: a record naming no icon describes nothing to put back.</summary>
    internal static TrayPromotionRecord? RecordIn(AppSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.TrayIconPromotionRestoreFor is { Length: > 0 } text &&
               Guid.TryParse(text, out var icon)
            ? new TrayPromotionRecord(icon, s.TrayIconPromotionRestoreValue)
            : null;
    }

    /// <summary>Puts a restore record into one settings object, or clears it. Separated from the
    /// handler so the round trip through the document can be exercised without a display.</summary>
    internal static void Store(AppSettings s, TrayPromotionRecord? record)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.TrayIconPromotionRestoreFor   = record?.Icon.ToString("B").ToUpperInvariant();
        s.TrayIconPromotionRestoreValue = record?.Promoted;
    }
}
