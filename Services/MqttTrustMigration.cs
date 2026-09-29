using System.Text.Json;
using System.Text.Json.Nodes;
using ZeroZero.Config;
using ZeroZero.Mqtt;

namespace FocusDesk.Services;

/// <summary>
/// Puts a certificate-trust mode the MQTT module no longer knows back on the platform's own trust,
/// in <c>mqtt.json</c>, before the module opens the file.
/// </summary>
/// <remarks>
/// <para>The module stores the mode by name and has only <c>System</c> and <c>AcceptAny</c>. A name it
/// does not know fails the whole read, and the module then sets the file aside and starts every
/// broker setting from its defaults. A number is read by position, so a number meant for a removed
/// pinned mode would land on <c>AcceptAny</c>.</para>
/// <para>Anything other than those two names therefore becomes <c>System</c>, never <c>AcceptAny</c>:
/// a pinned mode proved which machine answered, and only the platform's own trust still does. The
/// rest of the document is left as it stands.</para>
/// </remarks>
internal static class MqttTrustMigration
{
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    /// <summary>Rewrites the file in <paramref name="directory"/> when its trust mode is neither of the
    /// two the module knows. True when it was rewritten.</summary>
    /// <remarks>A missing, unreadable or malformed file is left for the module, which already copes
    /// with each.</remarks>
    public static bool Apply(string directory)
    {
        string path = Path.Combine(directory, MqttSettingsFile.DefaultFileName);

        JsonObject? root;
        try
        {
            if (!File.Exists(path)) return false;
            root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }

        // The module reads property names in any casing, so the lookup has to as well.
        if (Property(root, nameof(MqttSettings.CertificateTrust)) is not JsonObject trust) return false;
        if (Property(trust, nameof(MqttCertificateTrust.Mode)) is { } mode && IsKnown(mode)) return false;

        trust[NameOf(trust, nameof(MqttCertificateTrust.Mode))] = nameof(MqttCertificateTrustMode.System);
        return AtomicFile.WriteText(path, root!.ToJsonString(WriteOptions)) is null;
    }

    private static bool IsKnown(JsonNode mode) =>
        mode.GetValueKind() == JsonValueKind.String
        && mode.GetValue<string>() is var name
        && (string.Equals(name, nameof(MqttCertificateTrustMode.System), StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, nameof(MqttCertificateTrustMode.AcceptAny), StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Property(JsonObject? node, string name) =>
        node is null ? null : node[NameOf(node, name)];

    private static string NameOf(JsonObject node, string name) =>
        node.Select(p => p.Key).FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase))
        ?? name;
}
