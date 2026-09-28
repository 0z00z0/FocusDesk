using System.Xml.Linq;

namespace FocusDesk.Tests;

/// <summary>The interface text as one shipped resource file holds it, for a test that composes a
/// line the way the running application would. The resource loader needs the Windows App Runtime,
/// which a test run does not load, so the file is read directly.</summary>
internal static class ShippedStrings
{
    /// <summary>A lookup over one language's file. A key the file does not hold throws, so a line
    /// asking for a missing string fails the test rather than composing around the key.</summary>
    public static Func<string, string> For(string language)
    {
        var document = XDocument.Parse(RepoFiles.Read(Path.Combine("Strings", language, "Resources.resw")));
        var values = document.Root!.Elements("data").ToDictionary(
            d => (string)d.Attribute("name")!,
            d => (string?)d.Element("value") ?? "",
            StringComparer.Ordinal);
        return key => values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"{language}: no string '{key}'");
    }
}
