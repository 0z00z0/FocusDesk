using FocusDesk.Helpers;
using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Win32;

namespace FocusDesk.UI;

/// <summary>
/// The App diagnostics page: the settings document, and the files a run leaves behind.
/// </summary>
/// <remarks>The log list is read from the folder rather than declared, so a rolled archive and the
/// installer's own log from an unattended update both appear without anything here naming them.</remarks>
public sealed partial class DiagnosticsSettingsPanel : UserControl
{
    public DiagnosticsSettingsPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Reload();
    }

    /// <summary>Rebuilds the log list. The folder gains files while the window is open.</summary>
    public void Reload() => FillLogMenu();

    private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) =>
        ExplorerLauncher.Reveal(SettingsService.FilePath);

    private void OnOpenSettingsFile(object sender, RoutedEventArgs e) =>
        ExplorerLauncher.Open(SettingsService.FilePath);

    /// <summary>Re-reads settings.json — a manual edit, or a file synced in from another machine.
    /// Every page reads the document again as it is navigated to, so the answer here is the whole
    /// of the feedback.</summary>
    private void OnReloadSettings(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Reload())
            NativeMessageBox.Information(IntPtr.Zero, AppInfo.Name, "Settings reloaded from disk.");
        else
            NativeMessageBox.Warning(IntPtr.Zero, AppInfo.Name,
                "Could not reload settings — the file is missing, unreadable, or from a newer build.");
    }

    /// <summary>The files in the log folder, newest first. An empty folder gets one disabled line
    /// rather than a menu that opens on nothing.</summary>
    private void FillLogMenu()
    {
        LogMenu.Items.Clear();

        foreach (string path in LogFiles())
        {
            var item = new MenuFlyoutItem { Text = Path.GetFileName(path) };
            item.Click += (_, _) => ExplorerLauncher.Open(path);
            LogMenu.Items.Add(item);
        }

        if (LogMenu.Items.Count == 0)
            LogMenu.Items.Add(new MenuFlyoutItem { Text = "No log has been written yet", IsEnabled = false });
    }

    private static IEnumerable<string> LogFiles()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogsDir)) return [];

            return Directory.EnumerateFiles(AppPaths.LogsDir)
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .ToList();
        }
        catch (Exception ex)
        {
            AppLog.Error("DiagnosticsSettingsPanel.LogFiles", ex);
            return [];
        }
    }
}
