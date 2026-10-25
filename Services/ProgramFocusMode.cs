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
