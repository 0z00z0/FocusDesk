using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// The notification-area menu's theme belongs to the shared tray host, which draws it in the
/// taskbar's theme at every rebuild. A theme set here as well fights the host's, and nothing fails.
/// </summary>
public class TrayMenuThemeTests
{
    [Fact]
    public void FocusDeskLeavesTheMenusThemeToTheHost() =>
        Assert.DoesNotContain("DarkChrome", RepoFiles.Read(@"UI\TrayIconHost.cs"));
}
