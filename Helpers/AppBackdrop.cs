using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FocusDesk.Helpers;

/// <summary>
/// Mica tinted with FocusDesk's own ground, for the Settings window and the dialog it opens. Stock
/// Mica Alt carries no tint on the dark theme, so the desktop wallpaper's colour shows through the
/// whole window while it is active: dark blue on the default wallpaper.
/// </summary>
/// <remarks>The fallback — an inactive window, or transparency effects switched off — is the same
/// tone, so the window changes only by the material's grain when it loses focus.</remarks>
internal sealed partial class AppBackdrop : SystemBackdrop
{
    // Enough of the tone to hold the wallpaper's hue down, and enough material left to read as Mica.
    private const float TintOpacity = 0.85f;

    private MicaController? _controller;

    /// <summary>Replaces the window's backdrop where Mica is supported, and leaves the window's own
    /// otherwise.</summary>
    public static void ApplyTo(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (MicaController.IsSupported()) window.SystemBackdrop = new AppBackdrop();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        var configuration = GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot);
        _controller = new MicaController { Kind = MicaKind.BaseAlt, TintOpacity = TintOpacity, LuminosityOpacity = 1f };
        Tone(configuration);
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(configuration);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        // A tint set by hand no longer follows the theme on its own.
        Tone(GetDefaultSystemBackdropConfiguration(target, xamlRoot));
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    private void Tone(SystemBackdropConfiguration configuration)
    {
        if (_controller is null) return;

        bool isDark = configuration.Theme switch
        {
            SystemBackdropTheme.Dark  => true,
            SystemBackdropTheme.Light => false,
            _                         => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        var ground = AppColors.FromPacked(AppPalette.GroundTint(isDark));
        _controller.TintColor = ground;
        _controller.FallbackColor = ground;
    }
}
