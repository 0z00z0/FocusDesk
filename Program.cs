using System.Runtime.CompilerServices;
using FocusDesk.Helpers;

namespace FocusDesk;

/// <summary>The entry point; the XAML compiler's own is switched off in FocusDesk.csproj.</summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Ends before the UI framework loads; only the watchdog task passes the argument.
        if (args.Contains(TaskDefinitions.WatchdogArg) && WatchdogTask.HoldsProbeOff)
            return;

        StartTheApplication();
    }

    // Separate so no UI framework type is resolved before the check.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartTheApplication() => XamlGeneratedProgram.XamlGeneratedMain();
}
