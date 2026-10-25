using System.Text.Json.Serialization;

namespace FocusDesk.Services;

/// <summary>Stored by member name, so never renamed; the first opens nothing and is what an absent mode reads as.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum ProgramFocusMode
{
    AllowListed,
    OpenMarkedAllowListed,
    OpenMarkedAllowAll,
}

/// <summary>The words a mode is published as in Home Assistant's select. A receiver's automation
/// compares against these literals, so they never change and never follow the interface
/// language.</summary>
internal static class ProgramFocusModes
{
    public static IReadOnlyList<string> Options { get; } =
        [.. Enum.GetValues<ProgramFocusMode>().Select(Option)];

    public static string Option(ProgramFocusMode mode) => mode switch
    {
        ProgramFocusMode.OpenMarkedAllowListed => "Open marked, allow only listed",
        ProgramFocusMode.OpenMarkedAllowAll    => "Open marked, allow all others",
        _                                      => "Allow only listed programs",
    };

    /// <summary>The mode an option names, or null for anything else.</summary>
    public static ProgramFocusMode? FromOption(string option)
    {
        foreach (var mode in Enum.GetValues<ProgramFocusMode>())
            if (Option(mode) == option) return mode;
        return null;
    }
}
