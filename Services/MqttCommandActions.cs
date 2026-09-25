namespace FocusDesk.Services;

/// <summary>The settings an inbound MQTT command can write — the same writes the Settings window
/// makes. Behind an interface so the command seam is testable without touching settings.json or a
/// display.</summary>
/// <remarks>A method whose write reaches a service that records a cause takes the entity id the
/// command arrived on, so the trail names the entity rather than the receiver. A plain settings
/// write records nothing and needs none.</remarks>
internal interface ISettingsActions
{
    /// <summary>Sets the display brightness, remembering what it was on the first change.</summary>
    void SetScreenBrightness(int percent, string entityId);

    /// <summary>Puts back the brightness remembered before the first change.</summary>
    void RestoreScreenBrightness(string entityId);

    /// <summary>Arms a focus session, or asks to cancel the one running — which opens the staged
    /// wait rather than ending it.</summary>
    void SetFocusSession(bool on, string entityId);

    /// <summary>How long the next session runs. Changing it while one runs changes the next one
    /// only.</summary>
    void SetFocusSessionMinutes(int minutes);

    /// <summary>Whether the next session blocks the network. Refused while one runs.</summary>
    void SetFocusBlocksNetwork(bool on);

    /// <summary>Whether the next session dims the screen. Refused while one runs.</summary>
    void SetFocusDimsScreen(bool on);

    /// <summary>Whether the next session covers every display. Refused while one runs.</summary>
    void SetFocusCoversScreen(bool on);

    /// <summary>Whether the next session blocks the mouse and keyboard. Refused while one runs.</summary>
    void SetFocusBlocksInput(bool on);

    /// <summary>Whether the next session limits the machine to the chosen programs. Refused while one
    /// runs.</summary>
    void SetFocusLimitsPrograms(bool on);
}

/// <summary>The live settings writes behind every inbound command.</summary>
internal sealed class MqttCommandActions : ISettingsActions
{
    /// <summary>Raised after a command has changed something, so the surface is republished without
    /// waiting for the next pass.</summary>
    public event Action? Changed;

    // Through the service rather than a plain write: the display is what changes, and the record of
    // what to put back is written inside it. A plain settings write would reach neither.
    public void SetScreenBrightness(int percent, string entityId)
    {
        ScreenBrightnessService.Set(percent, ActionCause.HomeAssistant(entityId));
        Raise();
    }

    public void RestoreScreenBrightness(string entityId)
    {
        ScreenBrightnessService.Restore(ActionCause.HomeAssistant(entityId));
        Raise();
    }

    // Through the service, like the two above: arming moves the display and writes the session down
    // before anything else happens.
    public void SetFocusSession(bool on, string entityId)
    {
        if (on) FocusSessionService.Arm(ActionCause.HomeAssistant(entityId));
        else FocusSessionService.RequestCancel(ActionCause.HomeAssistant(entityId));
        Raise();
    }

    public void SetFocusSessionMinutes(int minutes) => Write(s => s.FocusSessionMinutes = minutes);

    public void SetFocusBlocksNetwork(bool on) => WriteUnlessSessionRunning(
        s => s.FocusBlocksNetwork = on, "which lever blocks the network");

    public void SetFocusDimsScreen(bool on) => WriteUnlessSessionRunning(
        s => s.FocusDimsScreen = on, "which lever dims the screen");

    public void SetFocusCoversScreen(bool on) => WriteUnlessSessionRunning(
        s => s.FocusCoversScreen = on, "which lever covers the screen");

    public void SetFocusBlocksInput(bool on) => WriteUnlessSessionRunning(
        s => s.FocusBlocksInput = on, "which lever blocks the mouse and keyboard");

    public void SetFocusLimitsPrograms(bool on) => WriteUnlessSessionRunning(
        s => s.FocusLimitsPrograms = on, "which lever limits the programs");

    /// <summary>A lever choice, refused while a session runs. Turning one off part-way through would
    /// either restore the screen while the session still claims to be running, or leave that lever's
    /// record parked with nothing owning it.</summary>
    /// <remarks>The refusal is a write that does not happen: the entity reflects its own value back
    /// after the debounce, so the receiver's switch returns to what the session is actually
    /// using.</remarks>
    private void WriteUnlessSessionRunning(Action<AppSettings> mutate, string what)
    {
        if (FocusSessionService.LeversAreLocked)
        {
            AppLog.Info($"Focus: {what} cannot change while a session is running.");
            Raise();
            return;
        }
        Write(mutate);
    }

    private void Write(Action<AppSettings> mutate)
    {
        SettingsService.Update(mutate);
        Raise();
    }

    private void Raise() => Changed?.Invoke();
}
