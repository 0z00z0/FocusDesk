using System.Text.Json;
using ZeroZero.Config.Sections;

namespace FocusDesk.Services;

/// <summary>
/// The settings document, held by the shared library's sectioned store: one file, one section per
/// Settings page, the section names spelled exactly as the file spells them. The store owns the
/// write, the lock, the quarantine of a document it cannot read, and its own <c>ConfigVersion</c>
/// key. This application's <c>Version</c> key is an ordinary top-level key the store never touches,
/// and it keeps its own job — refusing a document written by a build that reads more than this one.
/// </summary>
/// <remarks>A section is bound by its exact spelling: one addressed in another case binds nothing
/// and hands back the type's defaults with nothing said, which is why <see cref="ReadSections"/>
/// refuses a document that reports a conflicting key rather than loading defaults over it.</remarks>
internal sealed class SettingsStore
{
    /// <summary>The store's own document key, which sits beside this application's
    /// <see cref="SettingsFile.VersionKey"/> rather than replacing it.</summary>
    public const string StoreVersionKey = "ConfigVersion";

    private static readonly Dictionary<string, SettingsStore> _open =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock _openLock = new();

    private readonly string _path;
    private readonly SectionedSettingsFile _document;

    private SettingsStore(string path)
    {
        _path = path;
        _document = Open(path);
    }

    /// <summary>The store for one document, opened once per path. A store already open is re-read
    /// first, so an edit made outside the application is picked up; a store just opened has read the
    /// file already, and reading it twice would quarantine an unreadable document twice.</summary>
    public static SettingsStore For(string path)
    {
        string full = Path.GetFullPath(path);
        lock (_openLock)
        {
            if (_open.TryGetValue(full, out var open))
            {
                open._document.Reload();
                return open;
            }

            var store = new SettingsStore(full);
            _open[full] = store;
            return store;
        }
    }

    private static SectionedSettingsFile Open(string path) =>
        new(new SectionedSettingsOptions(Path.GetDirectoryName(path) ?? ".", Path.GetFileName(path))
        {
            Version      = SettingsFile.CurrentVersion,
            SectionOrder = SettingsFile.SectionNames,
        });

    /// <summary>The settings the document carries, or null when there is nothing usable: no file, a
    /// document from a newer build, or one whose section spelling collides with the one this build
    /// reads. Null never means "write defaults" — <see cref="Write"/> refuses on the same
    /// grounds.</summary>
    public AppSettings? Read()
    {
        if (!File.Exists(_path)) return null;

        // A document that will not parse has already been set aside by the store as it opened.
        // Nothing is returned rather than the section types' defaults: the section defaults are not
        // this application's defaults, so handing them back would silently switch every lever off
        // and set the session length to zero.
        if (!Parses()) return null;

        if (RefusalToTouchTheDocument() is { } refusal)
        {
            AppLog.Error(refusal, new NotSupportedException(Path.GetFileName(_path)));
            return null;
        }

        var (file, conflict) = ReadSections();
        if (conflict is null) return file.ToSettings();

        AppLog.Error($"settings.json carries a section this build cannot bind: {conflict}. It is left "
                   + "untouched and not written to, because binding it would load defaults over the "
                   + "settings it holds.",
                     new NotSupportedException(conflict));
        return null;
    }

    /// <summary>Writes every section whose content has moved, and reports whether all of them
    /// landed. Never throws: callers are settings handlers with nothing to unwind.</summary>
    public bool Write(AppSettings settings)
    {
        if (RefusalToTouchTheDocument() is { } refusal)
        {
            AppLog.Error(refusal, new NotSupportedException(Path.GetFileName(_path)));
            return false;
        }

        if (ReadSections().Conflict is { } conflict)
        {
            AppLog.Error($"settings.json is not written: {conflict}. Writing would leave the document "
                       + "carrying two spellings of one section.",
                         new NotSupportedException(conflict));
            return false;
        }

        // The store creates no directory of its own, and every writer here creates its own.
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
        catch (Exception ex) { AppLog.Error("SettingsStore: the settings folder could not be created", ex); }

        return WriteSections(_document, SettingsFile.From(settings));
    }

    /// <summary>Why the document must be neither read nor written, or null when it may be. Both
    /// version keys are honoured: this application's own <see cref="SettingsFile.VersionKey"/>, which
    /// says which shape the file is in, and the store's <see cref="StoreVersionKey"/>, which says
    /// which document layout wrote it.</summary>
    private string? RefusalToTouchTheDocument()
    {
        if (OwnVersionOnDisk() is { } version && version > SettingsFile.CurrentVersion)
            return $"settings.json declares version {version}, newer than this build reads "
                 + $"(version {SettingsFile.CurrentVersion}). It is left untouched and not written to.";

        return _document.IsFromNewerVersion
            ? $"settings.json declares {StoreVersionKey} {_document.DocumentVersion}, newer than this "
            + $"build writes ({SettingsFile.CurrentVersion}). It is left untouched and not written to."
            : null;
    }

    private int? OwnVersionOnDisk()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return SettingsFile.ReadVersion(doc.RootElement);
        }
        catch
        {
            return null;   // unreadable: the store has already set it aside
        }
    }

    private bool Parses()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The sections as one file shape, and the first section whose spelling the document
    /// contradicts. A conflicting key is the store's one silent failure, so it is read here and
    /// refused rather than left to hand back defaults.</summary>
    private (SettingsFile File, string? Conflict) ReadSections()
    {
        string? conflict = null;

        T Bind<T>(string name) where T : class, new()
        {
            var section = _document.Section<T>(name);
            if (conflict is null && section.ConflictingKey is { } key)
                conflict = $"'{name}' against the '{key}' the file carries";
            return section.Read();
        }

        var file = new SettingsFile
        {
            Focus      = Bind<SettingsFile.FocusGroup>(SettingsFile.FocusKey),
            Screen     = Bind<SettingsFile.ScreenGroup>(SettingsFile.ScreenKey),
            Appearance = Bind<SettingsFile.AppearanceGroup>(SettingsFile.AppearanceKey),
            Window     = Bind<SettingsFile.WindowGroup>(SettingsFile.WindowKey),
        };
        return (file, conflict);
    }

    /// <summary>Offers every section to the store, which lays the document down once for each one
    /// whose content has moved and leaves the file alone for the rest.</summary>
    private static bool WriteSections(SectionedSettingsFile document, SettingsFile file)
    {
        bool       landed  = true;
        Exception? first   = null;
        string?    stalled = null;

        void Put<T>(string name, T value) where T : class, new()
        {
            var result = document.Section<T>(name).Write(value);
            if (result.Saved) return;

            landed   = false;
            first  ??= result.Error;
            stalled ??= name;
        }

        Put(SettingsFile.FocusKey,      file.Focus);
        Put(SettingsFile.ScreenKey,     file.Screen);
        Put(SettingsFile.AppearanceKey, file.Appearance);
        Put(SettingsFile.WindowKey,     file.Window);

        if (landed) return true;

        AppLog.Error($"settings.json: the '{stalled}' section could not be saved.",
                     first ?? new IOException("the settings document could not be written"));
        return false;
    }
}
