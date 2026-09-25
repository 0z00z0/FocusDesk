using FocusDesk.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ZeroZero.Mqtt.WinUI;

namespace FocusDesk.UI;

/// <summary>
/// The MQTT page: the shared settings panel, with the sentence above it that says why the feature
/// exists here. Everything inside the panel is the module's — the broker, the device identity, the
/// probe and the publish groups.
/// </summary>
/// <remarks>Named a page rather than a panel, unlike its two siblings, because what it holds is the
/// shared control of that name.</remarks>
public sealed partial class MqttSettingsPage : UserControl
{
    private readonly MqttPublisher? _mqtt;

    public MqttSettingsPage()
    {
        InitializeComponent();

        _mqtt = MqttService.Current;
        if (_mqtt is null)
        {
            Unavailable.Visibility = Visibility.Visible;
            Panel.Visibility = Visibility.Collapsed;
            return;
        }

        Panel.Initialise(new MqttPanelSetup
        {
            Settings        = _mqtt.Settings,
            Groups          = _mqtt.Groups,
            TopicRoot       = MqttEntityCatalog.TopicRoot,
            Activity        = _mqtt.Activity,
            ConnectionState = () => _mqtt.State,
            PublishNow      = _mqtt.PublishNowAsync,
            // Both run the real paths: the panel's device-id dialogue has already promised the old
            // entities are removed, which only the connection's apply keeps, and the announced entity
            // set is baked into the retained document, which only a republish rewrites.
            ConnectionChanged  = _mqtt.ApplyConnection,
            PublishSetChanged  = _mqtt.Republish,
            DefaultDeviceName  = _mqtt.DefaultDeviceName,
            PublishTitle       = "Publish to MQTT",
            PublishDescription = "Publishes the focus session and the screen to an MQTT broker.",
            PublishGroupsInfo  =
                "Each group covers the entities from the Settings page it is named after. Switching "
              + "one off marks its entities unavailable; switching it back on restores them.",
            DeviceIdConsequence =
                "Every automation and dashboard card pointing at the old entities has to be repointed "
              + "by hand.",
            CommandLabel = _mqtt.Entities.NameOf,
            Log          = new AppLogSink(),
        });
    }

    /// <summary>Re-reads the live connection as the page comes back on screen. Nothing outside the
    /// panel writes its settings file, so the stored values are as the panel left them and only what
    /// the link is doing can have moved.</summary>
    public void Refresh()
    {
        if (_mqtt is not null) Panel.Refresh();
    }

    /// <summary>Drops an in-flight probe. From the window's <c>Closed</c> and nowhere else: it is
    /// final, and the shell keeps the page for the next visit.</summary>
    public void Cancel()
    {
        if (_mqtt is not null) Panel.Cancel();
    }
}
