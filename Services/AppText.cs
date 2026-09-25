using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.Windows.ApplicationModel.Resources;

namespace FocusDesk.Services;

/// <summary>
/// The interface text in <c>Strings\&lt;language&gt;\Resources.resw</c>, read from code through the
/// Windows App SDK resource loader. Every key is flat, with no dot: the format reads the segment after
/// a dot as a property of a named element.
/// </summary>
/// <remarks>
/// <para>A key nothing answers comes back as the key itself, so a missing string or an index that did
/// not load shows up in a screenshot rather than as a blank control.</para>
/// <para>The application's merged index holds its own strings under the bare <c>Resources</c> map, as
/// the shared MQTT component's are held under its own name; the index is opened both as the
/// application's default and by its file name, and whichever answers first is used.</para>
/// </remarks>
internal static class AppText
{
    private const string Map = "Resources";

    private static readonly Lazy<IReadOnlyList<Func<string, string?>>> _probes = new(OpenProbes);

    /// <summary>The text for <paramref name="key"/> in the interface language, or the key where none
    /// is found.</summary>
    public static string Get(string key)
    {
        foreach (var probe in _probes.Value)
        {
            try
            {
                if (probe(key) is { Length: > 0 } text) return text;
            }
            catch
            {
                // A map that throws on a key rather than answering null is still a map without it.
            }
        }
        return key;
    }

    /// <summary>A whole sentence with numbered placeholders, filled for the current culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);

    private static IReadOnlyList<Func<string, string?>> OpenProbes()
    {
        try { return Probes(); }
        catch
        {
            // The resource loader is unavailable where no Windows App Runtime is loaded; the keys
            // themselves then stand in.
            return [];
        }
    }

    // Kept out of line so a missing resource assembly fails inside the caller's try rather than
    // when the caller itself is compiled.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<Func<string, string?>> Probes()
    {
        var probes = new List<Func<string, string?>>();
        foreach (Func<ResourceManager> open in new Func<ResourceManager>[]
                 { () => new ResourceManager(), () => new ResourceManager("FocusDesk.pri") })
        {
            ResourceMap root;
            try { root = open().MainResourceMap; }
            catch { continue; }

            ResourceMap? map = null;
            try { map = root.TryGetSubtree(Map); } catch { }
            if (map is not null) probes.Add(key => map.TryGetValue(key)?.ValueAsString);
        }
        return probes;
    }
}
