using FocusDesk.Helpers;
using ZeroZero.Lifecycle;

namespace FocusDesk.Services;

/// <summary>Single source of truth for the per-user data location,
/// <c>%AppData%\FocusDesk\</c>, taken from the shared library's product data path.</summary>
/// <remarks>Reading <see cref="DataDir"/> creates the folder. Settings and the small state files sit
/// at the top level; the log sits in <c>Logs</c>, which <c>nlog.config</c> names for itself.</remarks>
internal static class AppPaths
{
    internal const string HistoryFolderName = "History";

    /// <summary>The folder <c>nlog.config</c> names for the log. Spelled here too, so anything
    /// writing beside the log — an installer's own log handed over as a path — lands in it.</summary>
    internal const string LogsFolderName = "Logs";

    internal static string DataDir { get; } = ProductDataPath.Root(AppInfo.Name);

    // Declared after DataDir: static initialisers run in textual order.
    internal static string HistoryDir { get; } = Path.Combine(DataDir, HistoryFolderName);

    internal static string LogsDir { get; } = Path.Combine(DataDir, LogsFolderName);

    /// <summary>Composes a path for a file or subdirectory name; neither creates nor checks for it.</summary>
    internal static string DataFile(string name) => Path.Combine(DataDir, name);

    /// <summary>Composes a path inside the History subfolder; neither creates nor checks for it.</summary>
    internal static string HistoryFile(string name) => Path.Combine(HistoryDir, name);

    /// <summary>Composes a path inside the Logs subfolder; neither creates nor checks for it.</summary>
    internal static string LogFile(string name) => Path.Combine(LogsDir, name);
}
