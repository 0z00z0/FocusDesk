using FocusDesk.Helpers;
using ZeroZero.Lifecycle;

namespace FocusDesk.Services;

/// <summary>Single source of truth for the per-user data location,
/// <c>%AppData%\FocusDesk\</c>, taken from the shared library's product data path.</summary>
/// <remarks>Reading <see cref="DataDir"/> creates the folder. Settings and the small state files sit
/// at the top level; the log sits in <c>Logs</c>, which <c>nlog.config</c> names for itself.</remarks>
internal static class AppPaths
{
    internal static string DataDir { get; } = ProductDataPath.Root(AppInfo.Name);

    /// <summary>Composes a path for a file or subdirectory name; neither creates nor checks for it.</summary>
    internal static string DataFile(string name) => Path.Combine(DataDir, name);
}
