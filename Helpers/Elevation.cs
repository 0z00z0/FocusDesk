using System.Security.Principal;
using FocusDesk.Services;

namespace FocusDesk.Helpers;

/// <summary>Whether this run holds administrator rights. The manifest asks for them, so it is a
/// reading rather than a question — but the input block and the firewall are refused outright
/// without them, and a lever that quietly does nothing is worse than one that says why.</summary>
internal static class Elevation
{
    private static readonly Lazy<bool> _elevated = new(() =>
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            AppLog.Error("Elevation.IsElevated", ex);
            return false;
        }
    });

    internal static bool IsElevated => _elevated.Value;
}
