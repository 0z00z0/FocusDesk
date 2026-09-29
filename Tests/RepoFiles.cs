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

    /// <summary>One method's whole text out of a source file, from its return type to its closing
    /// brace or, for an expression-bodied member, to its semicolon. Fails where the method is not
    /// found, so a rename cannot leave a guard reading nothing.</summary>
    public static string Member(string source, string name)
    {
        var signature = System.Text.RegularExpressions.Regex.Match(source, $@"\b(?:void|bool)\s+{name}\s*\(");
        Xunit.Assert.True(signature.Success, $"{name} was not found");

        int close = source.IndexOf(')', signature.Index);
        int brace = source.IndexOf('{', close);
        int arrow = source.IndexOf("=>", close, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
            return source[signature.Index..(source.IndexOf(';', arrow) + 1)];

        int depth = 0;
        for (int i = brace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[signature.Index..(i + 1)];
        }
        throw new InvalidOperationException($"{name} has no closing brace");
    }
}
