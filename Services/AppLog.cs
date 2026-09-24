using System.Runtime.CompilerServices;
using NLog;

namespace FocusDesk.Services;

/// <summary>
/// The app's Info/Error log at <c>%AppData%\FocusDesk\Logs\app.log</c>, a facade over NLog configured
/// by <c>nlog.config</c> beside the exe.
/// </summary>
/// <remarks>
/// Several FocusDesk processes can hold <c>app.log</c> open at once — a watchdog probe and a running
/// instance, for one — so the shipped config pairs <c>keepFileOpen="false"</c> with a
/// <c>RetryingWrapper</c>; without both, a concurrent writer loses lines silently. Not
/// <c>concurrentWrites="true"</c> — NLog 6 removed it and ignores it without a word.
/// </remarks>
internal static class AppLog
{
    /// <summary>Spelt again in <c>nlog.config</c>, which cannot read it. The two must resolve to the
    /// same path.</summary>
    internal const string FileName = "app.log";

    /// <summary>Width of the class column. A name longer than this pushes the message right on that
    /// line rather than being truncated.</summary>
    internal const int ClassColumnWidth = 24;

    /// <summary>The event property carrying the class column's value.</summary>
    internal const string ClassProperty = "class";

    /// <summary>Rendered in the class column when an event carries no class, so an empty column is
    /// visibly empty rather than looking like a formatting fault.</summary>
    internal const string UnknownClass = "-";

    /// <summary>The class column. <c>whenEmpty</c> sits INSIDE the padding wrapper: applied outside
    /// it, the padding makes an empty value non-empty and the placeholder never renders.</summary>
    internal const string ClassColumn =
        "${pad:padding=-24:inner=${event-properties:item=" + ClassProperty +
        ":whenEmpty=" + UnknownClass + "}}";

    internal const string LoggerName = "FocusDesk";

    private static readonly Logger _log = Initialise();

    /// <summary>Touching <see cref="_log"/> first forces <see cref="Initialise"/> to have run; a
    /// sibling logger resolved before that gets whatever NLog happened to have.</summary>
    internal static Logger NamedLogger(string name)
    {
        _ = _log.Name;
        return LogManager.GetLogger(name);
    }

    // callerFilePath costs nothing at run time and survives async boundaries; see
    // docs/build-notes.md ("AppLog.cs") for the ${callsite} cost measurement.
    public static void Info(string message, [CallerFilePath] string callerFilePath = "") =>
        Write(_log, LogLevel.Info, message, callerFilePath);

    // The exception is folded into the message rather than passed as NLog's exception argument, so
    // the layout needs no ${exception} clause.
    public static void Error(string source, Exception? ex, [CallerFilePath] string callerFilePath = "") =>
        Write(_log, LogLevel.Error, ex is null ? source : $"{source}\n{ex}", callerFilePath);

    /// <summary>Writes one entry carrying the class column. Kept on a shared path so a future
    /// second facade over the same log cannot drift from this one's format.</summary>
    internal static void Write(Logger log, LogLevel level, string message, string callerFilePath)
    {
        var entry = LogEventInfo.Create(level, log.Name, message);
        entry.Properties[ClassProperty] = ClassOf(callerFilePath);
        log.Log(entry);
    }

    /// <summary>The class column's value for a caller's source path. File names are unique across the
    /// tree, so the file names its class.</summary>
    internal static string ClassOf(string callerFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(callerFilePath.AsSpan());
        // XAML code-behind arrives as Page.xaml.cs, whose class is Page.
        if (name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            name = name[..^".xaml".Length];

        return name.IsEmpty ? UnknownClass : name.ToString();
    }

    // Runs from the _log field initialiser and must never throw — see docs/build-notes.md
    // ("AppLog.cs") for why.
    private static Logger Initialise()
    {
        try
        {
            // A missing or unparseable nlog.config leaves NLog unconfigured — it then logs nothing,
            // with no fallback. See docs/build-notes.md ("AppLog.cs").
            _ = LogManager.Configuration;
        }
        catch { /* left unconfigured; see the remarks above */ }

        return LogManager.GetLogger(LoggerName);
    }
}
