using System;
using System.IO;
using Xunit;
using ZeroZero.Mqtt;

namespace FocusDesk.Tests;

/// <summary>
/// The certificate-trust field in FocusDesk's own <c>mqtt.json</c>, opened the way
/// <c>MqttPublisher</c> opens it: <see cref="Services.MqttTrustMigration.Apply"/>, then
/// <see cref="MqttSettingsFile.In"/>. The shared library's own tests prove the field itself.
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

    private MqttSettingsFile Open()
    {
        Services.MqttTrustMigration.Apply(_dir);
        return MqttSettingsFile.In(_dir);
    }

    private void WriteDocument(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, MqttSettingsFile.DefaultFileName), json);
    }

    [Fact]
    public void AcceptingAnyCertificateSurvivesARoundTripThroughFocusDesksOwnFile()
    {
        using (var store = Open())
        {
            store.Update(s => s.CertificateTrust = MqttCertificateTrust.AcceptAny);
        }

        using var reopened = Open();

        Assert.Equal(MqttCertificateTrustMode.AcceptAny, reopened.Read().CertificateTrust.Mode);
    }

    /// <summary>A document written before this field existed carries no certificate-trust key. It
    /// must read as the platform's own trust, never as accepting any certificate.</summary>
    [Fact]
    public void ADocumentWithNoCertificateTrustKeyReadsAsPlatformTrust()
    {
        WriteDocument("{ \"Enabled\": true, \"Host\": \"broker.invalid\" }");

        using var store = Open();

        Assert.Equal(MqttCertificateTrustMode.System, store.Read().CertificateTrust.Mode);
    }

    /// <summary>A document holding a pinned mode the module no longer has — by name, as the module
    /// wrote it, or by its old position — opens on the platform's own trust with every other broker
    /// setting kept. Never accepting any certificate, and never a file set aside.</summary>
    [Theory]
    [InlineData("\"Thumbprint\"")]
    [InlineData("\"Certificate\"")]
    [InlineData("1")]
    [InlineData("2")]
    public void ARemovedPinnedModeOpensAsPlatformTrustWithTheBrokerKept(string mode)
    {
        WriteDocument("{ \"Enabled\": true, \"Host\": \"broker.invalid\", \"Port\": 8883, "
                    + "\"CertificateTrust\": { \"Mode\": " + mode + ", \"Thumbprint\": \"AABBCC\", "
                    + "\"Certificate\": \"\" } }");

        using var store = Open();
        var settings = store.Read();

        Assert.Equal(MqttCertificateTrustMode.System, settings.CertificateTrust.Mode);
        Assert.Null(store.File.LastQuarantinePath);
        Assert.True(settings.Enabled);
        Assert.Equal("broker.invalid", settings.Host);
        Assert.Equal(8883, settings.Port);
    }
}
