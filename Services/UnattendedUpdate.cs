namespace FocusDesk.Services;

/// <summary>
/// How Setup is started for the application's own update. Setup runs with no wizard and replaces the
/// files this process holds, so everything it is told has to be on the command line before the
/// process goes.
/// </summary>
/// <remarks>The installer script reads <see cref="StartedByApplicationSwitch"/> as
/// <c>{param:UPDATEFROMAPP}</c>. Neither side can read the other's constant, so
/// <c>Tests\UnattendedUpdateTests.cs</c> holds the pair together.</remarks>
internal static class UnattendedUpdate
{
    /// <summary>Names this run to Setup, which cannot otherwise tell it from a winget or scheduled
    /// run carrying the same silent switches. Setup restarts the application it killed only for
    /// this one.</summary>
    internal const string StartedByApplicationSwitch = "/UPDATEFROMAPP=1";

    /// <summary>Setup's log. An unattended run leaves no other trace of what it did.</summary>
    internal const string InstallerLogFileName = "update-install.log";

    internal static string InstallerLogPath => AppPaths.LogFile(InstallerLogFileName);

    /// <summary>
    /// What Setup is started with. <c>/SILENT</c> rather than <c>/VERYSILENT</c>: neither shows a
    /// wizard, and the progress window is the only thing on screen in the seconds between the
    /// application closing and its successor starting. <c>/SUPPRESSMSGBOXES</c> because an error box
    /// under a silent run has no wizard behind it and no process left to own it. <c>/LOG</c> because
    /// nothing else records what an unattended run did.
    /// </summary>
    /// <remarks>One string: the shared update component hands the command line to Setup as it
    /// stands. The log path is quoted, because a data folder can carry a space.</remarks>
    internal static string Arguments(string installerLogPath) =>
        $"/SILENT /SUPPRESSMSGBOXES /NORESTART {StartedByApplicationSwitch} /LOG=\"{installerLogPath}\"";
}
