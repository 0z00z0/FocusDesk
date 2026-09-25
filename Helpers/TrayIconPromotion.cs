using Microsoft.Win32;

namespace FocusDesk.Helpers;

/// <summary>Where an icon's promotion state is kept. Behind a seam so the policy above it is
/// exercisable without touching the shell's own settings.</summary>
/// <remarks>Every member reports failure by returning rather than throwing: nothing here is worth
/// taking a caller down for, and the whole mechanism is unsupported.</remarks>
internal interface ITrayPromotionStore
{
    /// <summary>Whether the icon is promoted, or null where the shell holds no value for it — which
    /// includes an icon the shell has never seen. Null is not false: a restore deletes what was
    /// never there rather than writing a zero.</summary>
    bool? Read(Guid icon);

    /// <summary>Sets the value. False where it could not be written.</summary>
    bool Write(Guid icon, bool promoted);

    /// <summary>Removes the value, leaving the shell's own default. False where it could not be
    /// removed.</summary>
    bool Clear(Guid icon);
}

/// <summary>What the previous state was, so switching the row off puts it back.</summary>
/// <param name="Icon">The icon the record was taken for. A record from another icon is ignored.</param>
/// <param name="Promoted">The value found, or null where the shell held none.</param>
internal readonly record struct TrayPromotionRecord(Guid Icon, bool? Promoted);

/// <summary>
/// Pulls the notification-area icon out of the overflow flyout, and puts back what was there before.
/// Windows publishes no interface for this, so it is done by writing the shell's own setting.
/// </summary>
/// <remarks>Undocumented throughout: the key, the value and when the shell reads them are all
/// observed rather than specified. Every path degrades to doing nothing, because the alternative is
/// an application that fails over a preference about where an icon sits.</remarks>
internal static class TrayIconPromotion
{
    /// <summary>
    /// Switches promotion on, recording what was there first. The record is the caller's to keep:
    /// it goes in the settings document so a restore survives a restart.
    /// </summary>
    /// <param name="existing">The record already held, or null where none is. An existing record is
    /// never overwritten — the first value seen is the one to put back.</param>
    /// <returns>The record to keep, or <paramref name="existing"/> unchanged where nothing was
    /// written.</returns>
    public static TrayPromotionRecord? TurnOn(
        ITrayPromotionStore store, Guid icon, TrayPromotionRecord? existing)
    {
        ArgumentNullException.ThrowIfNull(store);

        var record = existing is { } held && held.Icon == icon
            ? held
            : new TrayPromotionRecord(icon, store.Read(icon));

        return store.Write(icon, true) ? record : existing;
    }

    /// <summary>
    /// Puts the promotion back at startup where the row is on and the shell's value has gone — a
    /// Windows update or a profile reset clears it and leaves the row saying on. A value the shell
    /// holds, false included, is left alone: a person dragging the icon into the overflow writes it.
    /// </summary>
    /// <param name="applied">True where the value was written again.</param>
    /// <returns>The record to keep, which is <paramref name="existing"/> unless a first record had
    /// to be taken.</returns>
    public static TrayPromotionRecord? Reapply(
        ITrayPromotionStore store, Guid icon, bool wanted, TrayPromotionRecord? existing,
        out bool applied)
    {
        ArgumentNullException.ThrowIfNull(store);

        applied = false;
        if (!wanted || store.Read(icon) is not null) return existing;

        var record = TurnOn(store, icon, existing);
        applied = store.Read(icon) == true;
        return record;
    }

    /// <summary>
    /// Switches promotion off by putting back what the record holds. A record taken for another icon,
    /// or none at all, leaves the shell alone: this only undoes what it did.
    /// </summary>
    /// <returns>Null once the record has been spent, or the record unchanged where nothing was
    /// written and it is still owed.</returns>
    public static TrayPromotionRecord? TurnOff(
        ITrayPromotionStore store, Guid icon, TrayPromotionRecord? record)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (record is not { } held || held.Icon != icon) return record;

        // Null means the shell held nothing, so the value goes rather than becoming a zero the shell
        // never wrote.
        bool restored = held.Promoted is { } promoted
            ? store.Write(icon, promoted)
            : store.Clear(icon);

        return restored ? null : record;
    }
}

/// <summary>
/// The shell's own promotion setting, under <c>HKCU\Control Panel\NotifyIconSettings</c>.
/// </summary>
/// <remarks>
/// <para>The subkeys are opaque numbers, so the icon is found by the <c>IconGuid</c> each one
/// carries rather than by its name or its path. A subkey appears only once the shell has seen the
/// icon, so there is nothing to write before the first run.</para>
/// <para><c>IsPromoted</c> is a DWORD. It is the whole of what is written; the snapshot and the
/// tooltip beside it belong to the shell.</para>
/// </remarks>
internal sealed class RegistryTrayPromotionStore : ITrayPromotionStore
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    private const string IconGuidValue = "IconGuid";
    private const string PromotedValue = "IsPromoted";

    public bool? Read(Guid icon)
    {
        try
        {
            using var key = OpenIcon(icon, writable: false);
            return key?.GetValue(PromotedValue) is int promoted ? promoted != 0 : null;
        }
        catch (Exception ex)
        {
            Log("read", ex);
            return null;
        }
    }

    public bool Write(Guid icon, bool promoted)
    {
        try
        {
            using var key = OpenIcon(icon, writable: true);
            if (key is null) return false;

            key.SetValue(PromotedValue, promoted ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            Log("write", ex);
            return false;
        }
    }

    public bool Clear(Guid icon)
    {
        try
        {
            using var key = OpenIcon(icon, writable: true);
            if (key is null) return false;

            // Deleting a value that is not there is not a failure: the end state is the one asked for.
            key.DeleteValue(PromotedValue, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            Log("clear", ex);
            return false;
        }
    }

    /// <summary>The subkey carrying this icon's GUID, or null where the shell has never seen it. The
    /// stored spelling is compared as a GUID rather than as text, so braces and case cannot
    /// decide it.</summary>
    private static RegistryKey? OpenIcon(Guid icon, bool writable)
    {
        using var root = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: false);
        if (root is null) return null;

        foreach (string name in root.GetSubKeyNames())
        {
            using var candidate = root.OpenSubKey(name, writable: false);
            if (candidate?.GetValue(IconGuidValue) is not string text) continue;
            if (!Guid.TryParse(text, out var found) || found != icon) continue;

            return root.OpenSubKey(name, writable);
        }

        return null;
    }

    private static void Log(string what, Exception ex) =>
        Services.AppLog.Error($"TrayIconPromotion: the shell's icon settings could not be {what}", ex);
}
