using System;
using System.Collections.Generic;
using FocusDesk.Helpers;
using FocusDesk.Services;
using Xunit;
using static FocusDesk.Tests.MeasuredWindows;

namespace FocusDesk.Tests;

/// <summary>
/// The decision for one window, from facts shaped like the measured listings on the development
/// machine. Every case here is one where a wrong answer reaches the person at the keyboard: a shell
/// window minimised leaves the machine unusable, an allowed program minimised stops the work the
/// session was for, and a program let through defeats the lever.
/// </summary>
public class ProgramGateRulesTests
{
    private const string Brave     = @"C:\Program Files\BraveSoftware\Brave-Browser\Application\brave.exe";
    private const string Vivaldi   = @"C:\Users\Someone\AppData\Local\Vivaldi\Application\vivaldi.exe";
    private const string Terminal  = @"C:\Program Files\Example\Terminal\terminal.exe";
    private const string Editor    = @"C:\Program Files\Example\editor.exe";
    private const string Mstsc     = @"C:\Windows\System32\mstsc.exe";
    private const string Update    = @"C:\Users\Someone\AppData\Local\Discord\Update.exe";
    private const string Discord   = @"C:\Users\Someone\AppData\Local\Discord\app-1.0.9212\Discord.exe";
    private const string BraveWebApp = "Brave._crx_abcdefghijklmnopqrstuvwxyz";
    private const string Claude    = "Claude_pzs8sxrjxfjjc";
    private const string Teams     = "MSTeams_8wekyb3d8bbwe";
    private const string Settings  = "windows.immersivecontrolpanel_cw5n1h2txyewy";
    private const string Keyboard  = "MicrosoftWindows.Client.CBS_cw5n1h2txyewy";

    private static FocusProgramEntry File(string path, bool run = true) =>
        new() { Kind = FocusProgramKind.ProgramFile, Id = path, CanRun = run, CanUseNetwork = true };

    private static FocusProgramEntry Package(string family, bool run = true) =>
        new() { Kind = FocusProgramKind.StorePackage, Id = family, CanRun = run };

    private static FocusProgramEntry Web(string identity, bool run = true) =>
        new() { Kind = FocusProgramKind.WebApp, Id = identity, CanRun = run };

    private static GateContext Context(params FocusProgramEntry[] entries) =>
        new(entries, FocusProgramAction.Minimise, WindowsFolder, FocusDeskPath, [],
            [@"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData", @"C:\Users\Someone",
             @"C:\Users\Someone\AppData\Local", @"C:\Users\Someone\AppData\Roaming"],
            family => family is Settings or Keyboard);

    private static GateOutcome Outcome(WindowFacts window, GateContext context) =>
        ProgramGateRules.Decide(window, context).Outcome;

    // ── Windows itself ──────────────────────────────────────────────────────────────────────────

    /// <summary>Minimising any of these leaves the machine without a file manager, a keyboard or a
    /// Settings app, with nothing on the list able to bring them back.</summary>
    [Fact]
    public void FileExplorerTheTouchKeyboardAndTheFramedSettingsApp_AreExempt()
    {
        var context = Context();

        Assert.Equal(GateOutcome.Exempt, Outcome(Window(@"C:\Windows\explorer.exe"), context));
        Assert.Equal(GateOutcome.Exempt, Outcome(
            Window(@"C:\Windows\SystemApps\MicrosoftWindows.Client.CBS_cw5n1h2txyewy\TextInputHost.exe", Keyboard), context));
        Assert.Equal(GateOutcome.Exempt, Outcome(
            Window(@"C:\Windows\System32\ApplicationFrameHost.exe",
                   appId: $"{Settings}!microsoft.windows.immersivecontrolpanel",
                   hosted: Process(@"C:\Windows\ImmersiveControlPanel\SystemSettings.exe", Settings)), context));
    }

    /// <summary>Remote Desktop is the one Windows program a session may limit. The command prompt
    /// stays usable whatever the list says.</summary>
    [Fact]
    public void RemoteDesktopIsLimitedUnlessAllowed_AndTheCommandPromptIsAlwaysExempt()
    {
        Assert.Equal(GateOutcome.Limited, Outcome(Window(Mstsc), Context()));
        Assert.Equal(GateOutcome.Allowed, Outcome(Window(Mstsc), Context(File(Mstsc))));
        Assert.Equal(GateOutcome.Exempt, Outcome(Window(@"C:\Windows\System32\cmd.exe"), Context()));
    }

    /// <summary>Elevation is no way past the list; Task Manager stays usable because it is part of
    /// Windows, not because it runs elevated.</summary>
    [Fact]
    public void AnElevatedProgramIsLimited_AndElevatedTaskManagerIsExempt()
    {
        Assert.Equal(GateOutcome.Limited, Outcome(Window(Editor, elevated: true), Context()));
        Assert.Equal(GateOutcome.Exempt, Outcome(Window(@"C:\Windows\System32\Taskmgr.exe", elevated: true), Context()));
    }

    [Fact]
    public void FocusDesksOwnWindowsAndAnUnreadableProcess_AreExempt()
    {
        var verdict = ProgramGateRules.Decide(Window(FocusDeskPath), Context());
        Assert.Equal(GateOutcome.Exempt, verdict.Outcome);
        Assert.Equal(GateExemption.FocusDesk, verdict.Exemption);

        Assert.Equal(GateExemption.Unidentifiable, ProgramGateRules.Decide(Window(""), Context()).Exemption);
    }

    // ── Browsers and web apps ───────────────────────────────────────────────────────────────────

    [Fact]
    public void BravesBrowserWindowBelongsToBravesRow()
    {
        Assert.Equal(GateOutcome.Allowed, Outcome(Window(Brave, appId: "Brave"), Context(File(Brave))));
        Assert.Equal(GateOutcome.Limited, Outcome(Window(Brave, appId: "Brave"), Context()));
    }

    /// <summary>A web app runs inside its browser's processes, so its own identity is the only thing
    /// that tells it apart. Allowing the browser must not let every web app in.</summary>
    [Fact]
    public void AWebAppBelongsToItsOwnRow_AndOneWithNoRowIsLimitedWhileItsBrowserIsAllowed()
    {
        var webAppWindow = Window(Brave, appId: BraveWebApp);

        Assert.Equal(GateOutcome.Allowed, Outcome(webAppWindow, Context(Web(BraveWebApp))));
        Assert.Equal(GateOutcome.Limited, Outcome(webAppWindow, Context(File(Brave))));
        Assert.Equal(GateOutcome.Limited, Outcome(Window(Brave, appId: "Brave._crx_zyxwvutsrqponmlkjihgfedcba"),
                                                  Context(File(Brave), Web(BraveWebApp))));
    }

    [Fact]
    public void VivaldisProfileIdentitiesFallThroughToTheVivaldiRow() =>
        Assert.Equal(GateOutcome.Allowed,
                     Outcome(Window(Vivaldi, appId: "Vivaldi.VHQIGU4VLVCIS7ZN4E3LBCTBKU"), Context(File(Vivaldi))));

    // ── Packages, dialogs and helpers ───────────────────────────────────────────────────────────

    [Fact]
    public void APackagedClaudeWindowBelongsToItsPackage()
    {
        var window = Window(@"C:\Program Files\WindowsApps\Claude_1.0.0.0_x64__pzs8sxrjxfjjc\app\claude.exe", Claude);

        Assert.Equal(GateOutcome.Allowed, Outcome(window, Context(Package(Claude))));
        Assert.Equal(GateOutcome.Limited, Outcome(window, Context()));
    }

    [Fact]
    public void TeamsEmbeddedBrowserCountsAsTeams() =>
        Assert.Equal(GateOutcome.Allowed, Outcome(
            Window(@"C:\Program Files (x86)\Microsoft\EdgeWebView\Application\msedgewebview2.exe", Teams),
            Context(Package(Teams))));

    /// <summary>An allowed terminal must not become a way to run anything: what it starts from
    /// elsewhere is judged on its own.</summary>
    [Fact]
    public void APowerShellWindowStartedFromAnAllowedTerminal_IsJudgedOnItsOwn() =>
        Assert.Equal(GateOutcome.Limited, Outcome(
            Window(@"C:\Program Files\PowerShell\7\pwsh.exe", ancestors: Process(Terminal)),
            Context(File(Terminal))));

    [Fact]
    public void AHelperStartedByAnAllowedProgramFromItsOwnFolder_IsAllowed() =>
        Assert.Equal(GateOutcome.Allowed, Outcome(
            Window(@"C:\Program Files\Example\Terminal\helper.exe", ancestors: Process(Terminal)),
            Context(File(Terminal))));

    [Fact]
    public void ADialogOwnedByAnAllowedProgramsWindow_IsAllowedFromAnotherProcess() =>
        Assert.Equal(GateOutcome.Allowed, Outcome(
            Window(@"C:\Program Files\Other\picker.exe", owner: Process(Editor)),
            Context(File(Editor))));

    /// <summary>A launcher stub starts the real program from a versioned folder beside it, which
    /// changes at every update.</summary>
    [Fact]
    public void ADiscordProcessInAVersionedFolder_MatchesAnAllowedUpdateExe() =>
        Assert.Equal(GateOutcome.Allowed, Outcome(Window(Discord), Context(File(Update))));

    /// <summary>A program in a shared root folder does not own that folder: allowing one program
    /// directly under Program Files must not allow every program installed there.</summary>
    [Fact]
    public void AProgramInASharedRootDoesNotOwnTheRoot() =>
        Assert.Equal(GateOutcome.Limited, Outcome(
            Window(@"C:\Program Files\Other\other.exe"), Context(File(@"C:\Program Files\lonely.exe"))));

    // ── The action ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ARowWithRunClearedIsLimitedWithItsOwnStoredAction()
    {
        var entry = File(Editor, run: false) with { WhenNotAllowed = FocusProgramAction.ForceClose };
        var verdict = ProgramGateRules.Decide(Window(Editor), Context(entry));

        Assert.Equal(GateOutcome.Limited, verdict.Outcome);
        Assert.Equal(FocusProgramAction.ForceClose, verdict.Action);
        Assert.Equal(Editor, verdict.Identifier);
    }
}
