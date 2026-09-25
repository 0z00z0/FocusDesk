namespace FocusDesk.Helpers;

/// <summary>
/// The notification-area icon's identity as handed to the tray host. Fixed for the life of the
/// product: the shell keys the icon's position, and whether it was pulled out of the overflow
/// flyout, on the identity it holds, and a changed value loses both with no way back.
/// </summary>
/// <remarks>Not an arbitrary GUID. The shared tray host derives one from the icon's name when none
/// is given — the first sixteen bytes of SHA-256 over the name in UTF-8 — and this is that value for
/// <c>FocusDesk</c>. Stating it explicitly takes the identity off the name without moving a single
/// installed icon. The identity the shell actually holds is <see cref="UI.TrayIconHost.ShellId"/>,
/// and anything asking the shell about the icon takes that one.</remarks>
internal static class TrayIconIdentity
{
    public static readonly Guid Value = new("ADAB45AC-EC29-6860-C480-EDA4F569A006");
}
