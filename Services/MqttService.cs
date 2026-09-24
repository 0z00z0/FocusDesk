namespace FocusDesk.Services;

/// <summary>
/// The one MQTT publisher for the process, so the tray menu, the Settings page and the power-mode
/// handler all reach the same connection rather than each building one.
/// </summary>
/// <remarks>Held rather than passed because every other service here is reached the same way, and
/// because the Settings window is opened from the notification-area menu, which carries no state of
/// its own to pass along.</remarks>
internal static class MqttService
{
    private static MqttPublisher? _publisher;

    /// <summary>The publisher, or null before <see cref="Start"/> and after <see cref="Stop"/>. A page
    /// that opens before the publisher exists shows the feature as unavailable rather than
    /// throwing.</summary>
    public static MqttPublisher? Current => _publisher;

    /// <summary>Builds the publisher and applies the stored broker settings. Once, at startup. A
    /// failure here leaves the rest of the application running: publishing is a way in and out of a
    /// session, not what a session depends on.</summary>
    public static void Start()
    {
        if (_publisher is not null) return;

        try { _publisher = new MqttPublisher(); }
        catch (Exception ex) { AppLog.Error("MqttService.Start", ex); }
    }

    /// <summary>Publishes offline, closes the socket and lets the settings file go. Called from the
    /// way out, before the process ends, so the receiver sees the device go away rather than timing
    /// out on its last will.</summary>
    public static void Stop()
    {
        var publisher = _publisher;
        _publisher = null;
        publisher?.Dispose();
    }

    /// <summary>Told by the host's power-mode handler. The connection does not subscribe to system
    /// events itself.</summary>
    public static void OnPowerResume() => _publisher?.OnPowerResume();
}
