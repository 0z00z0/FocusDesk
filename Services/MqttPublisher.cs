using FocusDesk.Helpers;
using ZeroZero.Mqtt;
using ZeroZero.Mqtt.Discovery;

namespace FocusDesk.Services;

/// <summary>
/// FocusDesk's MQTT publisher: the module's connection and discovery publisher, wired to this
/// application's entity table, its publish groups and the settings an inbound command writes. The
/// protocol, the endpoint sweep, the document and the ledger are the module's; everything domain
/// shaped is here.
/// </summary>
/// <remarks>
/// <para>Inert until the module's own settings say publishing is on and a broker host is set.</para>
/// <para>The focus session's entities are the only way out of a session, so what is wired here is
/// what makes the application's own description true.</para>
/// </remarks>
internal sealed class MqttPublisher : IDisposable
{
    /// <summary>How often the surface is signalled while nothing else has moved. The remaining-minutes
    /// sensor counts down on a clock and nothing raises an event for it, so without this the receiver
    /// holds the value from the last stage change. An unchanged payload is deduped, so a signal that
    /// finds nothing moved costs nothing.</summary>
    private static readonly TimeSpan SurfaceInterval = TimeSpan.FromSeconds(30);

    private readonly MqttSettingsFile _settings;
    private readonly PublishGroupSet _groups;
    private readonly DiscoveryPublisher _publisher;
    private readonly MqttConnection _connection;
    private readonly MqttCommandActions? _ownSettingsActions;
    private readonly Timer _surfaceTimer;

    private int _disposed;

    /// <param name="settings">The settings seam an inbound command writes through; the live one when
    /// null.</param>
    public MqttPublisher(ISettingsActions? settings = null)
    {
        var log = new AppLogSink();

        Directory.CreateDirectory(AppPaths.DataDir);
        _settings = MqttSettingsFile.In(AppPaths.DataDir);

        // Read once, as the store opened: a document it could not parse has already been copied aside
        // and the broker settings are back at their defaults, which is a silent loss otherwise.
        if (_settings.File.LastQuarantinePath is { Length: > 0 } quarantined)
            AppLog.Error($"mqtt.json could not be read and was set aside as "
                       + $"{Path.GetFileName(quarantined)}. The broker settings start from their "
                       + "defaults.", null);

        _groups = new PublishGroupSet(_settings, MqttPublishGroups.Declared);

        if (settings is null)
        {
            _ownSettingsActions = new MqttCommandActions();
            settings = _ownSettingsActions;
        }

        Entities = MqttEntityCatalog.Build(new MqttEntitySources
        {
            // No per-pass cache: the display object behind both of these caches its own WMI answer,
            // and the settings read is in memory. Eight entities, nothing to amortise.
            Surface      = () => SurfaceReader.Read(),
            Capabilities = SurfaceReader.Capabilities,
            Settings     = settings,
        });

        MqttConnection? connection = null;

        // Nothing Retired, Migrating or RetiredChannels: FocusDesk has never published, so there is no
        // installed base and no retained topic from an earlier shape to carry across.
        _publisher = new DiscoveryPublisher(new DiscoveryPublisherSetup
        {
            IsConnected       = () => connection?.IsConnected ?? false,
            TopicRoot         = MqttEntityCatalog.TopicRoot,
            Device            = new DiscoveryDevice("ZeroZero Software", AppInfo.Name, AppInfo.Version),
            Origin            = new DiscoveryOrigin(AppInfo.Name, AppInfo.Version,
                                    SupportUrl: "https://github.com/0z00z0/FocusDesk"),
            Entities          = Entities,
            Ledger            = DiscoveryLedgerFile.In(AppPaths.DataDir),
            Groups            = _groups,
            SetChannelsAsync  = (channels, ct) => connection!.SetChannelsAsync(channels, ct),
            SetCommandTargets = targets => connection!.SetCommandTargets(targets),
            Log               = log,
        });

        connection = new MqttConnection(new MqttConnectionSetup
        {
            TopicRoot         = MqttEntityCatalog.TopicRoot,
            Channels          = _publisher.Channels(),
            CommandTargets    = _publisher.CommandTargets(),
            Subscriptions     = [_publisher.BirthMessage(DiscoveryPrefix())],
            Listener          = _publisher,
            DefaultDeviceName = machine => $"{AppInfo.Name} ({machine})",
            CommandRefused    = OnCommandRefused,
            Log               = log,
        });
        _connection = connection;

        // A write from an inbound command reflects at once rather than waiting for the next signal.
        if (_ownSettingsActions is { } own) own.Changed += PublishSurfaceNow;

        // The two sources that move a published value without a command behind them: the session's own
        // clock reaching a stage, and the display level changing from the Settings page or from Windows.
        FocusSessionService.Changed += PublishSurfaceNow;
        ScreenBrightnessService.Changed += PublishSurfaceNow;

        // Every broker edit the panel commits comes back through here, and Apply is idempotent, so a
        // change that leaves the projection identical costs nothing and never bounces the socket.
        _settings.Changed += OnSettingsChanged;
        _connection.Apply(_settings.Read().Connect());

        _surfaceTimer = new Timer(_ => PublishSurfaceNow(), null, SurfaceInterval, SurfaceInterval);
    }

    /// <summary>The module's settings store, for the panel. The panel writes through it, never
    /// around it.</summary>
    public IMqttSettingsStore Settings => _settings;

    /// <summary>The declared publish groups and their state, for the panel.</summary>
    public PublishGroupSet Groups => _groups;

    /// <summary>When something last reached the broker, and what the broker last asked for.</summary>
    public MqttActivity Activity => _connection.Activity;

    /// <summary>What the connection is doing. Asked rather than held: the link comes and goes on its
    /// own, so a cached answer is stale the moment the page stops looking.</summary>
    public MqttConnectionState State => _connection.State;

    /// <summary>Raised on every connection state change, on whichever background thread made it.</summary>
    public event Action<MqttConnectionState>? StateChanged
    {
        add    => _connection.StateChanged += value;
        remove => _connection.StateChanged -= value;
    }

    public bool IsConnected => _connection.IsConnected;

    /// <summary>The entity table in force, for the panel's entity-id-to-name lookup and for tests.</summary>
    public MqttEntitySet Entities { get; }

    /// <summary>The name published for this machine when the device-name box is empty. One expression,
    /// so the panel's placeholder and what the publisher falls back to cannot disagree.</summary>
    public string DefaultDeviceName => $"{AppInfo.Name} ({Environment.MachineName})";

    /// <summary>Re-applies the connection from the stored settings. What the panel's
    /// <c>ConnectionChanged</c> is wired to, and what keeps its device-id promise: the ledger evicts
    /// the superseded identity by what it actually published.</summary>
    public void ApplyConnection()
    {
        // The birth-message filter carries the discovery prefix, so a prefix change needs the
        // subscription rebuilt; it takes effect at the next connect, which the apply below causes.
        _connection.SetSubscriptions([_publisher.BirthMessage(DiscoveryPrefix())]);
        _connection.Apply(_settings.Read().Connect());
    }

    /// <summary>Re-announces the document. What a group toggle comes through: the announced entity set
    /// is baked into the retained document, so republishing state alone would leave the broker with
    /// the set captured at connect time.</summary>
    public void Republish() => _publisher.Republish();

    /// <summary>Every channel, dedupe bypassed. What "Publish now" needs, where nothing leaving the
    /// machine is indistinguishable from a dead connection.</summary>
    public Task<bool> PublishNowAsync() => _connection.PublishNowAsync();

    /// <summary>
    /// Signals a publish of the settings and session values. The connection's per-channel publish does
    /// not check the link itself and logs an error per channel when it is down; nothing is lost by
    /// skipping, because every connect sends every channel's current value regardless of dedupe.
    /// </summary>
    public void PublishSurfaceNow()
    {
        if (_connection.IsConnected) _connection.RequestPublish();
    }

    /// <summary>The host's power-mode handler calls this. The connection does not subscribe to system
    /// events itself, because the unsubscribe lifetime belongs to the host.</summary>
    public void OnPowerResume() => _connection.OnPowerResume();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _surfaceTimer.Dispose();
        _settings.Changed -= OnSettingsChanged;
        FocusSessionService.Changed -= PublishSurfaceNow;
        ScreenBrightnessService.Changed -= PublishSurfaceNow;
        if (_ownSettingsActions is { } own) own.Changed -= PublishSurfaceNow;

        // Teardown is synchronous, bounded and idempotent, and publishes offline before the socket goes.
        _connection.Dispose();
        _publisher.Dispose();
        _settings.Dispose();
    }

    /// <summary>The discovery prefix the birth-message filter is composed from. A stored prefix that is
    /// blank means the module's own default, not an empty segment.</summary>
    private string DiscoveryPrefix() =>
        _settings.Read().DiscoveryPrefix is { Length: > 0 } prefix
            ? prefix
            : MqttSettings.DefaultDiscoveryPrefix;

    private void OnSettingsChanged() => ApplyConnection();

    private void OnCommandRefused(MqttCommandRefusal refusal) =>
        AppLog.Info($"MQTT: {Entities.NameOf(refusal.EntityId)} refused ({refusal.Outcome})"
                  + (refusal.Detail is { Length: > 0 } detail ? $": {detail}" : "."));
}
