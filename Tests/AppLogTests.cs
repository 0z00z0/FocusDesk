using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FocusDesk.Helpers;
using FocusDesk.Services;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// Guards the shipped nlog.config and the AppLog facade over it. Every failure this file catches is
/// silent in production — NLog ignores an unknown attribute and logs nothing at all when the config
/// is missing or unparseable — so "it built" is not evidence that it logs.
/// </summary>
public class AppLogTests
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static XElement ShippedFileTargetElement() =>
        XDocument.Load(RepoFiles.Find("nlog.config"))
                 .Descendants()
                 .Single(t => t.Name.LocalName == "target" && (string?)t.Attribute(Xsi + "type") == "File");

    private static RetryingTargetWrapper WrapperOf(LoggingConfiguration config, string name = "appfile") =>
        (RetryingTargetWrapper)config.FindTargetByName(name)!;

    private static FileTarget FileTargetOf(LoggingConfiguration config, string name = "appfile") =>
        (FileTarget)WrapperOf(config, name).WrappedTarget!;

    /// <summary>RetryCount/RetryDelayMilliseconds are Layout&lt;int&gt;, so they compare as rendered text.</summary>
    private static string Rendered(Layout<int> value) => value.Render(LogEventInfo.CreateNullEvent());

    /// <summary>The shipped file with its comments stripped, so an assertion about what the config
    /// does not say is not defeated by a comment warning readers off that very spelling.</summary>
    private static string SettingsTextOfShippedConfig() =>
        Regex.Replace(File.ReadAllText(RepoFiles.Find("nlog.config")), "<!--.*?-->", string.Empty,
            RegexOptions.Singleline);

    [Fact]
    public void LoggerNameIsFocusDesk() =>
        Assert.Equal(AppInfo.Name, AppLog.LoggerName);

    [Fact]
    public void NLogConfigWritesUnderFocusDesksOwnAppDataFolder()
    {
        var fileName = ShippedFileTargetElement().Attribute("fileName")!.Value;

        Assert.Equal($@"${{specialfolder:folder=ApplicationData}}\{AppInfo.Name}\Logs\app.log",
            fileName, ignoreCase: true);
    }

    [Fact]
    public void ClassColumn_IsPaddedToTheDeclaredWidth()
    {
        // The width is a literal inside the shipped layout string, independent of AppLog's own
        // ClassColumnWidth constant, so the two can drift apart silently without this.
        var layout = ShippedFileTargetElement().Attribute("layout")!.Value;

        Assert.Contains($"padding=-{AppLog.ClassColumnWidth}", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedConfig_IsConcurrentWriterSafe()
    {
        // Open-per-write plus a bounded retry is what makes concurrent appends safe: NLog's
        // keepFileOpen="true" default silently loses lines when several FocusDesk processes append
        // to one file at once.
        var config = ShippedNLogConfig.LoadStrictly();
        var wrapper = Assert.IsType<RetryingTargetWrapper>(config.FindTargetByName("appfile"));

        Assert.Equal("5", Rendered(wrapper.RetryCount));
        Assert.Equal("20", Rendered(wrapper.RetryDelayMilliseconds));
        Assert.False(FileTargetOf(config).KeepFileOpen,
            "keepFileOpen must stay false — an exclusive handle makes sibling FocusDesk processes " +
            "(a watchdog probe, an ordinary duplicate launch) lose their log lines silently.");
    }

    [Fact]
    public void ShippedConfig_DoesNotUseNLog5sRemovedConcurrentWritesAttribute() =>
        // NLog 6 has no FileTarget.concurrentWrites; writing it here would parse, do nothing, and
        // still look like a concurrency setting.
        Assert.DoesNotContain("concurrentWrites", SettingsTextOfShippedConfig(),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ShippedConfig_DoesNotUseTheAgeBasedRetentionThatDeletesAJustArchivedLog() =>
        // Re-adding this parses, looks like the retention that was asked for, and silently destroys
        // a log file the moment it is archived.
        Assert.DoesNotContain("maxArchiveDays", SettingsTextOfShippedConfig(),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ShippedConfig_TimestampStaysGregorianUnderAnyThreadCulture()
    {
        // ${date} defaults to InvariantCulture, but an empty culture= falls back to the thread
        // culture and stamps a non-Gregorian year. ar-SA (Umm al-Qura) tells the two apart; en-GB
        // does not.
        var layout = FileTargetOf(ShippedNLogConfig.LoadStrictly()).Layout;

        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("ar-SA");
            var rendered = layout.Render(LogEventInfo.Create(LogLevel.Info, "x", "message"));

            Assert.StartsWith($"[{DateTime.Now.Year}-", rendered);
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    // Rotation and retention - driven, not parsed

    /// <summary>
    /// A throwaway copy of the log. Every write goes through a freshly loaded copy of the shipped
    /// config with the file name redirected here, so nothing reaches the real per-user log
    /// directory, and each call re-probes the file's age exactly as a restarted process would.
    /// </summary>
    private sealed class TempTrail : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), $"fd-nlogconfig-{Guid.NewGuid():N}");

        public string AppFile => Path.Combine(Dir, "app.log");

        private const string CallerFile = @"X:\src\SomeCaller.cs";

        public TempTrail() => Directory.CreateDirectory(Dir);

        /// <summary>Writes through <see cref="AppLog.Write"/>, the shipped config's one writer.</summary>
        public void Write(params string[] messages)
        {
            var config = ShippedNLogConfig.LoadStrictly();
            FileTargetOf(config).FileName = AppFile;
            var factory = new LogFactory { Configuration = config };
            try
            {
                foreach (var message in messages)
                    AppLog.Write(factory.GetLogger(AppLog.LoggerName), LogLevel.Info, message, CallerFile);
                factory.Flush();
            }
            finally { factory.Shutdown(); }
        }

        public static void Age(string file, int days)
        {
            var when = DateTime.Now.AddDays(-days);
            File.SetCreationTime(file, when);
            File.SetLastWriteTime(file, when);
        }

        public string Archive(int daysAgo) =>
            Path.Combine(Dir, $"app_{DateTime.Now.AddDays(-daysAgo):yyyy-MM-dd}_00.log");

        public void PlantArchive(int daysAgo)
        {
            var path = Archive(daysAgo);
            File.WriteAllText(path, $"an archive from {daysAgo} days ago");
            Age(path, daysAgo);
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }

    [Fact]
    public void ShippedConfig_ActuallyRollsToANewFileOnADayBoundary()
    {
        // Driven rather than asserted on attributes: a config can carry archiveEvery and still not
        // roll, because NLog reads the file's birth time rather than the entries in it.
        using var trail = new TempTrail();
        trail.Write("an entry from yesterday");

        TempTrail.Age(trail.AppFile, 1);

        trail.Write("an entry from today");

        var archive = trail.Archive(1);
        Assert.True(File.Exists(archive), $"app.log did not roll: no {Path.GetFileName(archive)}.");
        Assert.Contains("yesterday", File.ReadAllText(archive), StringComparison.Ordinal);

        var active = File.ReadAllText(trail.AppFile);
        Assert.Contains("today", active, StringComparison.Ordinal);
        Assert.DoesNotContain("yesterday", active, StringComparison.Ordinal);
    }

    [Fact]
    public void ShippedConfig_ActuallyKeepsSevenDailyArchivesAndDeletesTheRest()
    {
        // Deletion is the half that fails silently: nothing in the app notices archives piling up.
        using var trail = new TempTrail();
        for (var age = 1; age <= 9; age++)
            trail.PlantArchive(age);

        // The trail's directory also holds files that are not archives of this log; the sweep must
        // reach only its own.
        var bystander = Path.Combine(trail.Dir, "settings.json");
        File.WriteAllText(bystander, "{}");
        TempTrail.Age(bystander, 400);

        trail.Write("an entry");

        Assert.True(File.Exists(bystander), "the sweep must not delete files that are not its archives.");
        for (var age = 1; age <= 7; age++)
            Assert.True(File.Exists(trail.Archive(age)),
                $"the {age}-day-old archive is one of the seven most recent and must survive.");
        Assert.False(File.Exists(trail.Archive(8)), "the eighth archive back must be deleted.");
        Assert.False(File.Exists(trail.Archive(9)), "the ninth archive back must be deleted.");
    }
}
