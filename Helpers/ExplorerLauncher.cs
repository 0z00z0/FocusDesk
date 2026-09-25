using System.Diagnostics;

namespace FocusDesk.Helpers;

/// <summary>Opens Windows Explorer at a file, selecting it where it exists.</summary>
internal static class ExplorerLauncher
{
    /// <summary>The <c>explorer.exe</c> arguments that reveal a path: the file selected where it
    /// exists, or its containing folder quoted where it does not, so the folder still opens before
    /// anything has written the file.</summary>
    internal static string SelectFileArguments(string filePath, bool fileExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (fileExists) return $"/select,\"{filePath}\"";

        string? folder = Path.GetDirectoryName(filePath);
        return $"\"{(string.IsNullOrEmpty(folder) ? filePath : folder)}\"";
    }

    /// <summary>Reveals a path in Explorer.</summary>
    internal static void Reveal(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName        = "explorer.exe",
                Arguments       = SelectFileArguments(filePath, File.Exists(filePath)),
                UseShellExecute = true,
            });
        }
        catch (Exception ex) { Services.AppLog.Error($"ExplorerLauncher.Reveal '{filePath}'", ex); }
    }

    /// <summary>Opens a path with whatever handles its type, falling back to revealing it on any
    /// failure — no association, a missing file, a handler that refuses. Nothing is put on screen:
    /// a missing file lands the user in the nearest folder that does exist.</summary>
    internal static void Open(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
        }
        catch { Reveal(filePath); }
    }
}
