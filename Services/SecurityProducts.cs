using System.Management;

namespace FocusDesk.Services;

/// <summary>
/// The folders of every product registered with Windows Security Center, read when a session arms,
/// so a security alert is never minimised out of sight.
/// </summary>
/// <remarks>Measured unelevated on one machine: Security Center is readable without administrator
/// rights and lists Windows Defender alone. A product whose path is not a file on disk — Defender's
/// own entry names a protocol — contributes only its reporting executable's folder.</remarks>
internal static class SecurityProducts
{
    private static readonly string[] Classes = ["AntiVirusProduct", "AntiSpywareProduct", "FirewallProduct"];

    /// <summary>The folders, or none where Security Center cannot be read.</summary>
    /// <param name="notFolders">Folders no product owns — the Windows folder and the shared roots — so
    /// a product installed straight into one of them does not exempt everything beside it.</param>
    public static IReadOnlyList<string> Folders(IReadOnlyList<string> notFolders)
    {
        var folders = new List<string>();
        foreach (string className in Classes)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    @"root\SecurityCenter2", $"SELECT pathToSignedProductExe, pathToSignedReportingExe FROM {className}");
                foreach (ManagementBaseObject product in searcher.Get())
                    using (product)
                        foreach (string property in new[] { "pathToSignedProductExe", "pathToSignedReportingExe" })
                            if (FolderOf(product[property] as string, notFolders) is { } folder
                                && !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                                folders.Add(folder);
            }
            catch (Exception ex)
            {
                // A class missing on a server edition, or a refused query: fewer exemptions, not a
                // failed session.
                AppLog.Error($"SecurityProducts.Folders({className})", ex);
            }
        }
        return folders;
    }

    internal static string? FolderOf(string? productPath, IReadOnlyList<string> notFolders)
    {
        if (string.IsNullOrWhiteSpace(productPath)) return null;
        string path = FocusAllowedPrograms.Normalise(Environment.ExpandEnvironmentVariables(productPath));
        if (!FocusAllowedPrograms.IsProgram(path)) return null;

        string? folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder)) return null;
        return notFolders.Any(not => FocusAllowedPrograms.Same(folder.TrimEnd('\\'), not.TrimEnd('\\')))
            ? null
            : folder;
    }
}
