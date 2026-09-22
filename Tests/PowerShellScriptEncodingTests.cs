using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Windows PowerShell reads a .ps1 with no byte-order mark as the machine's ANSI code page, where
/// the third byte of a UTF-8 em dash decodes to a right double quotation mark — a character
/// PowerShell accepts as a string delimiter. One such character opens a string that never closes,
/// and the file fails to parse before a single line of it runs. An even number of them happens to
/// cancel out, so a file can carry the fault for months and break the moment a paragraph is edited.
/// The release installer is built by exactly such a script, so the failure lands on a release.
/// </summary>
public class PowerShellScriptEncodingTests
{
    [Fact]
    public void EveryPowerShellScriptIsPlainAscii()
    {
        var offenders = RepositoryScripts()
            .Select(path => new { path, offending = NonAsciiLines(path) })
            .Where(x => x.offending.Length > 0)
            .Select(x => $"{Path.GetRelativePath(RepositoryRoot, x.path)}: lines {string.Join(", ", x.offending)}")
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "PowerShell scripts must be ASCII only:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static string[] RepositoryScripts() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.ps1", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar)
                          .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    private static int[] NonAsciiLines(string path) =>
        File.ReadAllLines(path, Encoding.UTF8)
            .Select((line, index) => new { line, number = index + 1 })
            .Where(x => x.line.Any(c => c > ''))
            .Select(x => x.number)
            .ToArray();

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FocusDesk.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
