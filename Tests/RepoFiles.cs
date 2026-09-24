namespace FocusDesk.Tests;

/// <summary>
/// Locates a file inside the source tree from a test run. Several suites assert against shipped
/// source — markup, the logging config, the installer scripts — and each needs the same walk.
/// </summary>
internal static class RepoFiles
{
    /// <summary>The repository root, found by probing upwards for the project file rather than by
    /// hard-coding the test output's depth.</summary>
    public static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "FocusDesk.csproj")))
                    return dir.FullName;

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root walking up from '{AppContext.BaseDirectory}'.");
        }
    }

    /// <summary>The full path of a file inside the source tree.</summary>
    public static string Find(string relativePath)
    {
        string candidate = Path.Combine(Root, relativePath);
        if (File.Exists(candidate)) return candidate;

        throw new FileNotFoundException(
            $"Could not locate '{relativePath}' under '{Root}'.", candidate);
    }

    /// <summary>The text of a file inside the source tree.</summary>
    public static string Read(string relativePath) => File.ReadAllText(Find(relativePath));
}
