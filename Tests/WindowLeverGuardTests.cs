using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Two guards held against the source, because what they stop is a call that must never appear, not
/// a value: no test reaches the live window inspector, event watch or window actions, and nothing in
/// the focus-app lever closes a window or ends a process.
/// </summary>
/// <remarks>A test that listed, minimised or closed a real window would act on whatever the person
/// running the suite has open. A close request or a process ended would lose unsaved work, which this
/// version of the lever promises never to do.</remarks>
public class WindowLeverGuardTests
{
    /// <summary>The live classes. A test uses the interfaces and fakes of its own.</summary>
    private static readonly string[] LiveClasses = ["WindowInspector", "WindowEvents", "WindowActions"];

    /// <summary>Every file of the lever's own code.</summary>
    private static readonly string[] LeverFiles =
    [
        Path.Combine("Helpers", "WindowInspector.cs"),
        Path.Combine("Helpers", "WindowEvents.cs"),
        Path.Combine("Helpers", "WindowActions.cs"),
        Path.Combine("Services", "ProgramGateRules.cs"),
        Path.Combine("Services", "ProgramGate.cs"),
        Path.Combine("Services", "FocusProgramLever.cs"),
    ];

    /// <summary>Calls that close a window or end a process, or post a message that could.</summary>
    private static readonly string[] Forbidden =
    [
        "WM_" + "CLOSE", "SC_" + "CLOSE", "WM_" + "SYSCOMMAND", "Post" + "Message(", "Send" + "Message",
        "Terminate" + "Process", ".Kill(", "Close" + "MainWindow", "End" + "Task", "Destroy" + "Window",
    ];

    [Fact]
    public void NoTestReachesTheLiveWindowClasses()
    {
        string tests = Path.Combine(RepoFiles.Root, "Tests");
        // This file names the lever's source files to read them, so it is left out of its own sweep.
        var files = Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
            .Where(p => !Path.GetFileName(p).Equals(nameof(WindowLeverGuardTests) + ".cs", StringComparison.Ordinal))
            .ToArray();
        // A sweep that found nothing would pass by reading nothing.
        Assert.True(files.Length > 10, $"only {files.Length} test files found");

        var offenders = files
            .SelectMany(p => LiveClasses
                .Where(name => Regex.IsMatch(File.ReadAllText(p), $@"\b{name}\b"))
                .Select(name => $"{Path.GetFileName(p)}: {name}"))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheLeverNeitherClosesAWindowNorEndsAProcess()
    {
        foreach (string file in LeverFiles)
        {
            string source = RepoFiles.Read(file);
            foreach (string call in Forbidden)
                Assert.False(source.Contains(call, StringComparison.Ordinal), $"{file} carries {call}");
        }
    }
}
