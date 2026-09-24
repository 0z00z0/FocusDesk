using System.Reflection;

namespace FocusDesk.Helpers;

/// <summary>Process-wide product identity. The one literal for the data folder, dialog titles and
/// anything else that names the product.</summary>
internal static class AppInfo
{
    public const string Name = "FocusDesk";

    /// <summary>The running build's version as a person reads it. The informational version carries
    /// a build metadata suffix where the build adds one, which is for a log rather than a
    /// footer.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppInfo).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString(3) ?? "";
    }
}
