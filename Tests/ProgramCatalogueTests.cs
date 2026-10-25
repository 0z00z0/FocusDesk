using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Xunit;

namespace FocusDesk.Tests;

/// <summary>
/// What the picker offers: the classes a Start menu entry falls into, one row per program, and the
/// property that decides whether choosing from it works — a row yields an identifier the lever can
/// match on.
/// </summary>
/// <remarks>Everything here runs against composed entries shaped like the measured Start menu.
/// Nothing reads this machine, no firewall rule is touched, and no settings document is
/// written.</remarks>
public class ProgramCatalogueTests
{
    private const string Windows = @"C:\Windows";
    private const string Menu    = @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs";

    private const string Editor  = @"C:\Program Files\Example\editor.exe";
    private const string Vivaldi = @"C:\Users\Someone\AppData\Local\Vivaldi\Application\vivaldi.exe";
    private const string BraveProxy = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\chrome_proxy.exe";
    private const string BraveWebApp = "Brave._crx_abcdefghijklmnopqrstuvwxyz";

    private static StartShortcut Shortcut(string name, string target, string arguments = "", string appId = "",
                                          bool exists = true) =>
        new($@"{Menu}\{name}.lnk", target, arguments, appId, exists);

    private static ProgramChoice? Offered(StartShortcut s) => ProgramCatalogue.FromShortcut(s, Windows);

    // ── Hidden ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Each of these is a Start entry nobody means when choosing a program to keep usable,
    /// or one whose file is not the program: offering it would put a row on the list that matches
    /// nothing a person works in.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\Example\unins000.exe", "")]
    [InlineData(@"C:\Program Files\Example\uninst.exe", "")]
    [InlineData(@"C:\Program Files\Example\uninstall.exe", "")]
    [InlineData(@"C:\Windows\System32\msiexec.exe", "/x {00000000-0000-0000-0000-000000000000}")]
    [InlineData(@"C:\Windows\Installer\{11111111-1111-1111-1111-111111111111}\icon.exe", "")]
    [InlineData(@"C:\Program Files\Example\help.chm", "")]
    [InlineData(@"C:\Program Files\Example", "")]
    [InlineData("", "")]
    [InlineData(@"C:\Windows\explorer.exe", @"C:\Users\Someone\Documents")]
    [InlineData(@"C:\Windows\System32\mmc.exe", @"C:\Windows\System32\virtmgmt.msc")]
    [InlineData(@"C:\Program Files\WindowsApps\Example_1.0.0.0_x64__abc\store.exe", "")]
    public void UninstallersInstallerCommandsAdvertisedShortcutsAndNonPrograms_AreHidden(string target, string arguments) =>
        Assert.Null(Offered(Shortcut("Hidden", target, arguments)));

    [Fact]
    public void AShortcutToAProgramThatIsGone_IsHidden() =>
        Assert.Null(Offered(Shortcut("Gone", Editor, exists: false)));

    /// <summary>A Store app Windows signs as its own is always usable, so a row for it would do
    /// nothing.</summary>
    [Fact]
    public void AStoreAppWindowsSignsAsItsOwn_IsHidden_AndAnyOtherIsLimitable()
    {
        var settings = new AppsFolderEntry("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
                                           "Settings", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel",
                                           "windows.immersivecontrolpanel_cw5n1h2txyewy", @"C:\Windows\ImmersiveControlPanel", true);
        var claude = new AppsFolderEntry("Claude_pzs8sxrjxfjjc!Claude", "Claude", "Claude_pzs8sxrjxfjjc!Claude",
                                         "Claude_pzs8sxrjxfjjc", @"C:\Program Files\WindowsApps\Claude_1.0.0.0_x64__pzs8sxrjxfjjc", false);

        Assert.Null(ProgramCatalogue.FromAppsFolder(settings));

        var offered = ProgramCatalogue.FromAppsFolder(claude);
        Assert.NotNull(offered);
        Assert.Equal(FocusProgramKind.StorePackage, offered!.Kind);
        Assert.Equal("Claude_pzs8sxrjxfjjc", offered.Id);
        Assert.Equal(ProgramOffer.Limitable, offered.Offer);
        Assert.Equal(@"shell:AppsFolder\Claude_pzs8sxrjxfjjc!Claude", offered.StartEntry);
    }

    // ── Windows programs ────────────────────────────────────────────────────────────────────────

    /// <summary>Windows programs are always usable, because limiting one could leave the machine
    /// unusable. Remote Desktop is the one exception.</summary>
    [Fact]
    public void WindowsProgramsAreAlwaysUsable_ExceptRemoteDesktop()
    {
        Assert.Equal(ProgramOffer.AlwaysUsable, Offered(Shortcut("Command Prompt", @"C:\Windows\System32\cmd.exe"))!.Offer);
        Assert.Equal(ProgramOffer.AlwaysUsable, Offered(Shortcut("Task Manager", @"C:\Windows\System32\Taskmgr.exe"))!.Offer);
        Assert.Equal(ProgramOffer.Limitable, Offered(Shortcut("Remote Desktop Connection", @"%windir%\system32\mstsc.exe".Replace("%windir%", Windows)))!.Offer);
        Assert.Equal(ProgramOffer.Limitable, Offered(Shortcut("Editor", Editor))!.Offer);
    }

    /// <summary>The one Windows file a session may limit, pinned: a second one added here is the next
    /// Windows program stopping being exempt.</summary>
    [Fact]
    public void TheLimitableWindowsFilesAreRemoteDesktopAlone() =>
        Assert.Equal([@"System32\mstsc.exe"], WindowsPrograms.Limitable);

    // ── Web apps ────────────────────────────────────────────────────────────────────────────────

    /// <summary>A web app runs inside its browser's processes, so only its own identity tells its
    /// window apart. Keyed on anything else, allowing it would allow the browser, or nothing.</summary>
    [Fact]
    public void AProxyShortcutWithAnAppId_IsAWebAppKeyedOnItsOwnIdentity()
    {
        var offered = Offered(Shortcut("Example web app", BraveProxy,
                                       "--profile-directory=Default --app-id=abcdefghijklmnopqrstuvwxyzabcdef",
                                       BraveWebApp));

        Assert.NotNull(offered);
        Assert.Equal(FocusProgramKind.WebApp, offered!.Kind);
        Assert.Equal(BraveWebApp, offered.Id);
        Assert.Equal(BraveProxy, offered.BrowserPath);
        Assert.Equal(ProgramOffer.Limitable, offered.Offer);
    }

    [Fact]
    public void AWebAppWithNoIdentityOfItsOwn_IsNotOffered() =>
        Assert.Null(Offered(Shortcut("Anonymous web app", BraveProxy, "--app-id=abcdef")));

    // ── One row per program ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void DuplicatesMerge_IntoOneRowNamedAfterThePlainShortcut()
    {
        var apps = new AppsFolderEntry(BraveWebApp, "Example web app (Apps)", BraveWebApp, "", "", false);
        var merged = ProgramCatalogue.Merge(
        [
            Offered(Shortcut("Vivaldi - Work", Vivaldi, "--profile-directory=\"Profile 1\"")),
            Offered(Shortcut("Vivaldi", Vivaldi)),
            Offered(Shortcut("Vivaldi - Home", Vivaldi, "--profile-directory=\"Profile 2\"")),
            Offered(Shortcut("Example web app", BraveProxy, "--app-id=abcdef", BraveWebApp)),
            ProgramCatalogue.FromAppsFolder(apps),
            ProgramCatalogue.FromAppsFolder(new AppsFolderEntry("Example.Notes_8wekyb3d8bbwe!App", "Notes", "Example.Notes_8wekyb3d8bbwe!App", "Example.Notes_8wekyb3d8bbwe", "", false)),
            ProgramCatalogue.FromAppsFolder(new AppsFolderEntry("Example.Notes_8wekyb3d8bbwe!Settings", "Notes settings", "Example.Notes_8wekyb3d8bbwe!Settings", "Example.Notes_8wekyb3d8bbwe", "", false)),
        ]);

        Assert.Equal(3, merged.Count);
        Assert.Equal("Vivaldi", merged.Single(c => c.Kind == FocusProgramKind.ProgramFile).Name);
        var webApp = merged.Single(c => c.Kind == FocusProgramKind.WebApp);
        Assert.Equal(BraveProxy, webApp.BrowserPath);
        Assert.Single(merged, c => c.Kind == FocusProgramKind.StorePackage);
    }

    [Fact]
    public void TheOpenMarkIsMatchedByIdentifier()
    {
        var merged = ProgramCatalogue.Merge(
            [Offered(Shortcut("Editor", Editor)), Offered(Shortcut("Vivaldi", Vivaldi))],
            runningPaths: [Vivaldi.ToUpperInvariant()]);

        Assert.Equal([Vivaldi, Editor], merged.Select(c => c.Id));
        Assert.True(merged[0].Running);
        Assert.False(merged[1].Running);
    }

    // ── What a chosen row yields ────────────────────────────────────────────────────────────────

    /// <summary>No name is ever stored: a display name is neither unique nor stable, and a lookup
    /// keyed on one breaks the moment the Start menu renames a program.</summary>
    [Fact]
    public void AStoredRowCarriesNoName()
    {
        string[] names = [.. typeof(FocusProgramEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(n => n.Contains("Name", System.StringComparison.OrdinalIgnoreCase))];

        Assert.Empty(names);
        Assert.Contains("_onChosen(chosen.Choice)",
                        RepoFiles.Read(System.IO.Path.Combine("UI", "ProgramPickerWindow.xaml.cs")),
                        System.StringComparison.Ordinal);
    }

    // ── Narrowing by typing ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryWordTypedHasToAppear_InTheNameOrTheDetail()
    {
        var all = ProgramCatalogue.Merge([Offered(Shortcut("Alpha", Editor)), Offered(Shortcut("Alpha Beta", Vivaldi))]);

        Assert.Equal(2, ProgramCatalogue.Match(all, "").Count);
        Assert.Equal([Vivaldi], ProgramCatalogue.Match(all, "beta alpha").Select(e => e.Id));
        Assert.Equal([Editor], ProgramCatalogue.Match(all, "example").Select(e => e.Id));
        Assert.Empty(ProgramCatalogue.Match(all, "nothing here"));
    }
}
