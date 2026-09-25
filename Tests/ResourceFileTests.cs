using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The two interface languages kept in step, read from the resource files that ship: every key in
/// both, the same placeholders in both, a comment on every entry, and hover texts and descriptions
/// inside their caps in both.
/// </summary>
/// <remarks>A key missing from one language shows its key on screen instead of text, and a
/// placeholder the call site does not supply throws when the string is formatted. Nothing else
/// catches either.</remarks>
public class ResourceFileTests
{
    /// <summary>Longest hover text, in characters, in any language.</summary>
    private const int HoverCap = 110;

    /// <summary>Longest description, in characters, in any language.</summary>
    private const int DescriptionCap = 140;

    private static readonly string[] Languages = ["en-GB", "nb-NO"];

    private sealed record Entry(string Value, string? Comment);

    private static Dictionary<string, Entry> Read(string language)
    {
        var document = XDocument.Parse(RepoFiles.Read(Path.Combine("Strings", language, "Resources.resw")));
        return document.Root!.Elements("data").ToDictionary(
            d => (string)d.Attribute("name")!,
            d => new Entry((string?)d.Element("value") ?? "", (string?)d.Element("comment")),
            StringComparer.Ordinal);
    }

    private static string[] Placeholders(string value) =>
        [.. Regex.Matches(value, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void EveryKeyIsInBothLanguages()
    {
        var english = Read("en-GB");
        var norwegian = Read("nb-NO");

        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), norwegian.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryKeyCarriesTheSamePlaceholdersInBothLanguages()
    {
        var english = Read("en-GB");
        var norwegian = Read("nb-NO");

        foreach (var (key, entry) in english)
            if (norwegian.TryGetValue(key, out var translated))
                Assert.True(Placeholders(entry.Value).SequenceEqual(Placeholders(translated.Value)), key);
    }

    [Fact]
    public void EveryEntryHasAComment_AndNoKeyHasADot()
    {
        foreach (string language in Languages)
            foreach (var (key, entry) in Read(language))
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Comment), $"{language}: {key} has no comment");
                // A string read from code takes a flat key: the format reads the segment after a dot
                // as a property of a named element.
                Assert.DoesNotContain('.', key);
            }
    }

    /// <summary>The caps hold per language: Norwegian runs longer than English, and a string that
    /// fits in one and not the other moves a panel nobody has looked at. The floors stop a sweep that
    /// found nothing from passing.</summary>
    [Fact]
    public void HoverTextsAndDescriptionsFitTheirCapsInEveryLanguage()
    {
        foreach (string language in Languages)
        {
            var entries = Read(language);
            var hovers = entries.Where(e => e.Key.EndsWith("Info", StringComparison.Ordinal)).ToList();
            var descriptions = entries.Where(e => e.Key.EndsWith("Description", StringComparison.Ordinal)).ToList();

            Assert.NotEmpty(hovers);
            Assert.NotEmpty(descriptions);
            Assert.All(hovers, e => Assert.True(e.Value.Value.Length <= HoverCap, $"{language}: {e.Key}"));
            Assert.All(descriptions, e => Assert.True(e.Value.Value.Length <= DescriptionCap, $"{language}: {e.Key}"));
        }
    }

    /// <summary>Both files are what the build indexes. A language folder the project does not list
    /// builds and never resolves.</summary>
    [Fact]
    public void BothLanguagesAreIndexedByTheProject()
    {
        string project = RepoFiles.Read("FocusDesk.csproj");

        foreach (string language in Languages)
            Assert.Contains($@"<PRIResource Include=""Strings\{language}\Resources.resw"" />", project, StringComparison.Ordinal);
    }
}
