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

    /// <summary>
    /// Being ASCII only stops a non-ASCII character from shipping, but says nothing about a script
    /// that carries no BOM at all: such a script still decodes correctly today because it happens
    /// to hold no byte above 0x7F, and silently regains the parse-failure risk the moment somebody
    /// adds one, with nothing here to catch it before it ships. Requiring the BOM up front closes
    /// that gap for every script the repository holds, present or future.
    /// </summary>
    [Fact]
    public void EveryPowerShellScriptHasAUtf8Bom()
    {
        var offenders = RepositoryScripts()
            .Where(path => !HasUtf8Bom(path))
            .Select(path => Path.GetRelativePath(RepositoryRoot, path))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "PowerShell scripts must carry a UTF-8 byte-order mark:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static bool HasUtf8Bom(string path)
    {
        Span<byte> head = stackalloc byte[Utf8Bom.Length];
        using var stream = File.OpenRead(path);
        var read = stream.Read(head);
        return read == Utf8Bom.Length && head.SequenceEqual(Utf8Bom);
    }

    private static string[] RepositoryScripts() =>
        Directory.EnumerateFiles(RepositoryRoot, "*.ps1", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar)
                          .Any(segment => segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                                       || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

    // 0x7F (DEL) is the top of the ASCII range; anything above it is non-ASCII. Written as
    // '\u007F' rather than the raw control character, which renders as an apparently-empty
    // literal and can be silently dropped by a tool that strips control characters.
    private const char AsciiUpperBound = '\u007F';

    private static int[] NonAsciiLines(string path) =>
        File.ReadAllLines(path, Encoding.UTF8)
            .Select((line, index) => new { line, number = index + 1 })
            .Where(x => x.line.Any(c => c > AsciiUpperBound))
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
