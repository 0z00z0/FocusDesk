using ZeroZero.Tray;

namespace FocusDesk.Helpers;

/// <summary>The shared tray host's placement members, behind a seam so the row's rule is
/// exercisable without the shell's own settings.</summary>
internal interface ITrayPlacement
{
    /// <summary>The identity the shell holds the icon by; null until the icon is registered.</summary>
    Guid? ShellId { get; }

    /// <summary>Where the shell's setting says the icon is drawn; null while the shell keeps no entry
    /// for it.</summary>
    TrayIconPlacement? Placement { get; }

    /// <summary>Writes the placement through the shared host. False where nothing was written: the
    /// setting already read as wanted, no single entry carries the icon, or the write failed.</summary>
    bool AskFor(TrayIconPlacement wanted);
}

/// <summary>Where the icon was drawn before the row first moved it, so switching the row off puts
/// that back.</summary>
/// <param name="Icon">The identity the record was taken for. A record from another identity is
/// ignored.</param>
/// <param name="Before">The placement the first write replaced.</param>
internal readonly record struct TrayPlacementRecord(Guid Icon, TrayIconPlacement Before);

/// <summary>
/// The Appearance row's rule for asking the shell to draw the icon beside the clock, and for
/// putting back what was there before. The writing itself is the shared tray host's.
/// </summary>
/// <remarks>The shared host remembers what it replaced only for the life of the process, so the
/// record is the caller's to keep in the settings document: switching the row off after a restart
/// still puts back the placement found before the first write.</remarks>
internal static class TrayIconPlacementRow
{
    /// <summary>
    /// Asks for the notification area, recording what was there first. Also what runs at startup
    /// while the row is on, which puts the icon back after the shell has lost the setting or seen
    /// the icon under a new identity.
    /// </summary>
    /// <param name="held">The record already kept, or null. A record for this identity is never
    /// overwritten: the first placement seen is the one to put back.</param>
    /// <returns>The record to keep.</returns>
    public static TrayPlacementRecord? TurnOn(ITrayPlacement tray, TrayPlacementRecord? held)
    {
        ArgumentNullException.ThrowIfNull(tray);

        if (tray.ShellId is not { } icon || tray.Placement is not { } before) return held;
        if (!tray.AskFor(TrayIconPlacement.NotificationArea)) return held;

        return held is { } kept && kept.Icon == icon ? kept : new TrayPlacementRecord(icon, before);
    }

    /// <summary>
    /// Puts back the placement the record holds. A record taken for another identity, or none at all,
    /// leaves the shell alone: this only undoes what the row did.
    /// </summary>
    /// <returns>Null once the setting reads as the record says, or the record unchanged while it is
    /// still owed.</returns>
    public static TrayPlacementRecord? TurnOff(ITrayPlacement tray, TrayPlacementRecord? held)
    {
        ArgumentNullException.ThrowIfNull(tray);

        if (held is not { } record || tray.ShellId != record.Icon) return held;

        tray.AskFor(record.Before);
        return tray.Placement == record.Before ? null : held;
    }
}
