using System;
using System.IO;
using Xunit;
using ZeroZero.Mqtt;

namespace FocusDesk.Tests;

/// <summary>
/// The certificate-trust field FocusDesk inherited from ZeroZero.Mqtt 0.8.0, through FocusDesk's own
/// storage wiring — the same <see cref="MqttSettingsFile.In"/> call <c>MqttPublisher</c> makes. The
/// shared library's own tests already prove the field's behaviour in full; this is a thin
/// confirmation that FocusDesk's own file carries it correctly, not a re-test of the library.
/// </summary>
public class MqttCertificateTrustSettingsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), $"focusdesk-mqtt-cert-trust-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void APinnedCertificateTrustSurvivesARoundTripThroughFocusDesksOwnFile()
    {
        using (var store = MqttSettingsFile.In(_dir))
        {
            store.Update(s => s.CertificateTrust = MqttCertificateTrust.ForThumbprint("AA BB CC"));
        }

        using var reopened = MqttSettingsFile.In(_dir);
        var trust = reopened.Read().CertificateTrust;

        Assert.Equal(MqttCertificateTrustMode.Thumbprint, trust.Mode);
        Assert.Equal("AABBCC", trust.Thumbprint.Replace(" ", "", StringComparison.Ordinal));
    }

    /// <summary>A document written before this field existed, or one nobody has ever opened the MQTT
    /// page on, carries no certificate-trust key. It must read as the platform's own trust, never as
    /// <see cref="MqttCertificateTrustMode.AcceptAny"/> — an upgrade must not quietly turn off
    /// certificate verification on an installation that never asked for it.</summary>
    [Fact]
    public void ADocumentWithNoCertificateTrustKeyReadsAsPlatformTrust()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, MqttSettingsFile.DefaultFileName);
        File.WriteAllText(path, "{ \"Enabled\": true, \"Host\": \"broker.invalid\" }");

        using var store = MqttSettingsFile.In(_dir);
        var trust = store.Read().CertificateTrust;

        Assert.Equal(MqttCertificateTrustMode.System, trust.Mode);
        Assert.NotEqual(MqttCertificateTrustMode.AcceptAny, trust.Mode);
    }
}
