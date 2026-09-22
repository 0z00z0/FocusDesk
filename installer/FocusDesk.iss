; Inno Setup script for FocusDesk.
;
; Per-user install (no admin required). The app itself is requireAdministrator and
; elevates at runtime; the installer does not. The optional "Run at startup" task is
; the ONLY thing that elevates, and only if the user ticks it (see RegisterStartupTask).
;
; Build via installer\build-installer.ps1, which publishes the app and passes
; /DPublishDir and /DAppVersion to ISCC.

#define AppName       "FocusDesk"
#define AppExe        "FocusDesk.exe"
#define AppPublisher  "ZeroZero Software"
#define AppUrl        "https://github.com/0z00z0/FocusDesk"
#define TaskName      "FocusDesk AutoStart"

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
; AppId uniquely identifies this app for upgrades/uninstall — do not change it. A new value would
; orphan every existing install: the old one would never uninstall and both would sit in Apps &
; features.
AppId={{160415AC-6292-4FA9-A3E8-160DE1F4E692}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Inno Setup 6 defaults DisableWelcomePage=yes, which hides the Welcome page entirely — so the
; studio banner (WizardImageFile) and the WelcomeLabel copy below would only ever appear on the
; Finished page. Show the Welcome page (one extra "Next" click on the way in).
DisableWelcomePage=no
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
; Per-user: installs under %LocalAppData%\Programs, no UAC for the install itself.
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=FocusDesk-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Setup icon and the studio wizard bitmaps. Commented out until FocusDesk has an icon and a banner
; of its own: SetupIconFile is Setup.exe's own file icon, and the two bitmaps are the studio-look
; wizard art. Inno fails the compile on a file it cannot find, so each line stays inert rather than
; naming a path that does not exist. Restore all three once the artwork lands.
;SetupIconFile=..\Assets\SetupIcon.ico
;WizardImageFile=wizard\wizimg-492x942.bmp
;WizardSmallImageFile=wizard\wizsmall-165x174.bmp
; Restart Manager is NOT used to close the running app. Setup runs unelevated
; (PrivilegesRequired=lowest) while the app is requireAdministrator, so Restart Manager cannot
; terminate it: it logs "Can use RestartManager to avoid reboot? No (1: Permission Denied)" and
; Setup gives up BEFORE the install phase — no program files and no uninstall key are written, so
; an upgrade attempted while the app is running silently does nothing at all.
; PrepareToInstall in [Code] stops the app itself, through an elevated taskkill, at the step that
; runs just before Setup's own in-use check would have.
CloseApplications=no
; Immaterial while CloseApplications=no (Setup only restarts what it closed), but kept explicit:
; the app is requireAdministrator, so Setup must never relaunch it — LaunchApp in [Code] owns the
; relaunch and does it through the elevated logon task where one exists.
RestartApplications=no

[Messages]
; ── ZeroZero Software studio voice ───────────────────────────────────────────
; British English, plain language, brand name exactly "ZeroZero Software". Only the strings below
; are overridden — every other wizard string keeps Inno's default English. The wizard font is
; deliberately not changed: the brand typeface lives only in pre-rendered bitmap surfaces, so the
; copy stays in the default dialog font the target machine is guaranteed to have.
WelcomeLabel2=This will install {#AppName} on your computer.%n%n{#AppName} installs just for your user account, so no administrator rights are needed to set it up.%n%nNo telemetry, no accounts, no subscriptions.
; The app has no window — it runs from the notification area (system tray). Both finished-page
; strings are set so the first-time user knows where to find it, whichever variant Inno shows
; (with or without a post-install run option).
; ASCII-only on purpose: this .iss has no UTF-8 BOM, so Inno Setup 6 reads it as ANSI — a U+2014 em
; dash would ship as mojibake. Use plain ASCII punctuation here.
; Says "installed", not "installed and running": the post-install launch is an elevated ShellExec
; that the user can cancel at the UAC prompt, so "running" isn't guaranteed.
FinishedLabelNoIcons={#AppName} is installed. Look for its icon in the notification area (the system tray, next to the clock); that's where you start a focus session and change its settings.
FinishedLabel={#AppName} is installed. Look for its icon in the notification area (the system tray, next to the clock); that's where you start a focus session and change its settings.
; Quiet studio sign-off, bottom-left of every wizard page.
BeveledLabel=ZeroZero Software - Small tools. Zero bloat.

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
; Per-user "All apps" Start-menu entry. IconFilename points at the exe itself, which embeds the
; icon via <ApplicationIcon> in the project file — the same pattern as the desktop shortcut below
; and UninstallDisplayIcon above. Pointing at a loose {app}\AppIcon.ico would not work: the icon
; publishes into {app}\Assets\, never the install root.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; Comment: "{#AppName}"
; Optional desktop shortcut (off by default; ticked via the task below).
Name: "{userdesktop}\{#AppName}";  Filename: "{app}\{#AppExe}"; IconFilename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "runstartup"; Description: "Run {#AppName} automatically at sign-in (starts elevated without a UAC prompt at boot)"; Flags: unchecked
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

; NOTE: launching the app is handled in [Code] (LaunchApp), not [Run]. A [Run] entry uses
; CreateProcess, which CANNOT start a requireAdministrator exe (fails with "elevation
; required"). LaunchApp starts it correctly — via the elevated logon task if one exists
; (no extra prompt), otherwise via ShellExec (the single UAC prompt the app needs).

[Code]
const
  TaskName         = '{#TaskName}';
  WatchdogTaskName = 'FocusDesk Watchdog';

var
  // True when PrepareToInstall found (and killed) a running instance. Lets a SILENT upgrade
  // restart the app it killed: without this, a background upgrade leaves the tray app dead until
  // the next sign-in.
  WasRunning: Boolean;

// True when the application started this run for its own update. That run is silent like a winget
// or scheduled one and cannot be told from them by WizardSilent, yet it differs in every way that
// matters here: a user asked for it, the application is elevated and exiting for it, and it expects
// to be started again afterwards. The switch is passed by the application's unattended update.
function StartedByTheApplication(): Boolean;
begin
  Result := ExpandConstant('{param:UPDATEFROMAPP|0}') = '1';
end;

function ScheduledTaskExists(): Boolean;
var
  ResultCode: Integer;
begin
  // Querying does not require elevation; exit code 0 = the task exists.
  Result := Exec('schtasks.exe', '/Query /TN "' + TaskName + '"', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function WatchdogTaskExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('schtasks.exe', '/Query /TN "' + WatchdogTaskName + '"', '',
                 SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

procedure RegisterStartupTask();
var
  ResultCode: Integer;
  Params: string;
begin
  // The app rewrites this task at startup with power-safe settings from full XML
  // (StopIfGoingOnBatteries=false etc. — the schtasks CLI defaults below make Task Scheduler
  // hard-kill the instance the moment AC drops at undock). If the task already exists, leave the
  // app-maintained definition alone — recreating it here would regress those flags until the app's
  // next startup repair.
  if ScheduledTaskExists() then exit;

  // A logon task with RL HIGHEST lets the elevated app auto-start with no boot-time UAC
  // prompt. Creating a HIGHEST task needs admin, so this one step elevates via 'runas'
  // (exactly one UAC prompt — and only because the user ticked "Run at startup").
  Params := '/Create /TN "' + TaskName + '" /TR "\"' + ExpandConstant('{app}\{#AppExe}') +
            '\"" /SC ONLOGON /RL HIGHEST /F';
  if not ShellExec('runas', 'schtasks.exe', Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    MsgBox('Could not create the startup task. You can still enable "Launch at startup" '
           + 'from the app''s tray menu later.', mbInformation, MB_OK);
end;

function ProcessIsRunning(const ExeName: string): Boolean;
var
  ResultCode: Integer;
begin
  // tasklist|find: exit 0 only when the named process is present. Works without
  // elevation (the image name is visible even for an elevated process).
  Result := Exec(ExpandConstant('{cmd}'),
                 '/C tasklist /FI "IMAGENAME eq ' + ExeName + '" /NH | find /I "' + ExeName + '"',
                 '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function AppIsRunning(): Boolean;
begin
  Result := ProcessIsRunning('{#AppExe}');
end;

// ---------------------------------------------------------------------------
// Retry block. Self-contained on purpose: a presence poll, an elevated termination attempt and the
// loop around them, every one of them named by executable, so another requireAdministrator
// single-instance installer can lift the three routines whole. Nothing beyond {#AppName} and
// {#AppExe} is baked in.
// ---------------------------------------------------------------------------

// True once ExeName is gone. taskkill returns as soon as termination is REQUESTED, and the consent
// prompt behind it can be declined outright, so presence is polled rather than assumed.
function WaitForProcessToExit(const ExeName: string): Boolean;
var
  i: Integer;
begin
  for i := 1 to 10 do
  begin
    if not ProcessIsRunning(ExeName) then
    begin
      Result := True;
      exit;
    end;
    Sleep(200);
  end;
  Result := False;
end;

// One elevated attempt at ending ExeName. Setup runs PrivilegesRequired=lowest while the
// application is requireAdministrator, so an unelevated taskkill is refused with "Access is denied".
procedure StopProcessElevated(const ExeName: string);
var
  ResultCode: Integer;
begin
  ShellExec('runas', ExpandConstant('{cmd}'), '/C taskkill /F /IM "' + ExeName + '"',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

// Empty once ExeName is gone, otherwise the message Setup stops on. PrepareToInstall's return value
// is terminal — Setup does not re-enter the callback, and the Preparing to Install page carries no
// Retry control of its own — so the retry happens here, inside the callback, before it returns.
// Retry re-tests presence and only then re-attempts the kill, so an application exited from its own
// icon raises no second consent prompt.
//
// A silent run has nobody to answer, so it takes the terminal message immediately: WizardSilent
// covers /SILENT, where Inno Setup still displays error boxes and a modal prompt would orphan
// itself behind no wizard, and the IDCANCEL default covers /SUPPRESSMSGBOXES. The two conditions
// are tested separately rather than in one expression, because Pascal Script does not guarantee
// short-circuit evaluation.
//
// ASCII only in both strings — see the note in [Messages].
function ConfirmProcessHasExited(const ExeName: string): String;
var
  Answer: Integer;
begin
  Result := '';
  while not WaitForProcessToExit(ExeName) do
  begin
    Answer := IDCANCEL;
    if not WizardSilent() then
      Answer := SuppressibleMsgBox(
        '{#AppName} is still running, so its files cannot be replaced.' + #13#10#13#10
        + 'Exit it from its icon in the notification area (the system tray, next to the clock), '
        + 'then choose Retry.',
        mbError, MB_RETRYCANCEL, IDCANCEL);
    if Answer <> IDRETRY then
    begin
      Result := '{#AppName} is still running, so its files cannot be replaced. Exit it from its '
              + 'icon in the notification area (the system tray, next to the clock), then run '
              + 'this installer again.';
      exit;
    end;
    if ProcessIsRunning(ExeName) then StopProcessElevated(ExeName);
  end;
end;

// ---------------------------------------------------------------------------
// The application's own update. Unattended by request: it was agreed to in the application's update
// dialog, so no wizard is shown and no message box can be answered.
// ---------------------------------------------------------------------------

// The application queues its own exit as it starts this run, so that exit is still in flight when
// Setup gets here. Waiting for it is what keeps the update unattended: the termination below is
// elevated, and neither its consent prompt nor the refusal further down has anybody to answer it.
// Roughly sixteen seconds, then the ordinary path takes over — a process still present after that
// is stuck rather than closing.
procedure WaitForTheStartingApplicationToExit();
var
  i: Integer;
begin
  if not StartedByTheApplication() then exit;
  for i := 1 to 8 do
    if WaitForProcessToExit('{#AppExe}') then exit;
end;

// Where a refusal is stated, since an unattended run states it nowhere else: the message box is
// suppressed and the application that asked for the update has exited. The next start reads this
// beside its own record of the attempt and reports both. The file name must stay equal to the
// refusal file name the application reads.
// ASCII only — see the note in [Messages].
procedure RecordTheRefusal();
var
  Dir: string;
begin
  Dir := ExpandConstant('{userappdata}\{#AppName}');
  if ForceDirectories(Dir) then
    SaveStringToFile(Dir + '\update-refused.txt',
                     'Setup installed nothing: {#AppName} was still running when it started.',
                     False);
end;

procedure StopAppAndRemoveStartupTask();
var
  ResultCode: Integer;
begin
  // Stopping the running (elevated) app and deleting its RL HIGHEST logon + watchdog tasks all
  // need admin, so do them together in one elevated cmd -> at most ONE UAC prompt on uninstall.
  // The watchdog task goes FIRST: it relaunches a missing app exe, so it must be gone before the
  // taskkill or it could resurrect the app mid-uninstall.
  ShellExec('runas', ExpandConstant('{cmd}'),
            '/C schtasks /Delete /TN "' + WatchdogTaskName + '" /F'
            + ' & taskkill /IM "{#AppExe}" /F'
            + ' & schtasks /Delete /TN "' + TaskName + '" /F',
            '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure LaunchApp();
var
  ResultCode, i: Integer;
begin
  if ScheduledTaskExists() then
  begin
    // The elevated logon task exists -> run it on demand to start the app elevated with NO extra
    // UAC prompt (scheduled tasks bypass the consent prompt).
    Exec('schtasks.exe', '/Run /TN "' + TaskName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    // BUT: a task created by plain `schtasks /Create` carries the schtasks default
    // DisallowStartIfOnBatteries=true until the app rewrites it power-safe on first run. On battery
    // the scheduler ACCEPTS the /Run but silently declines to launch the action — the exact "app
    // didn't start after install" report. /Run's own exit code is 0 either way, so verify the app
    // actually came up instead: poll briefly and only fall through to a direct launch if it did not.
    for i := 1 to 6 do
    begin
      if AppIsRunning() then exit;
      Sleep(500);
    end;
  end;
  // No task, or the task-run didn't bring the app up (battery-blocked) -> launch directly. 'runas'
  // raises the UAC consent dialog to the foreground (the app is requireAdministrator); 'open' also
  // works but the dialog can appear behind the installer window and be missed.
  ShellExec('runas', ExpandConstant('{app}\{#AppExe}'), '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Kill any running instance BEFORE files are replaced so nothing is locked. The app is
  // requireAdministrator (elevated), so a non-elevated taskkill is refused with "Access is denied".
  // Elevate via runas — one UAC prompt, then the kill succeeds and the install continues without
  // locked-file errors.
  //
  // This runs from PrepareToInstall, not ssInstall. Order measured from a Setup log:
  // PrepareToInstall -> Restart Manager in-use check -> ssInstall -> file copy. Both code steps
  // precede the copy, but only PrepareToInstall precedes the in-use check that is refused on this
  // elevated app and takes Setup down with it before anything installs; it is also the only step
  // that can stop Setup with a readable message, as the return below does.
  //
  // The application's own update comes through here with its exit already requested, so that exit
  // is waited for FIRST — before anything reads the process or elevates to end it.
  WaitForTheStartingApplicationToExit();
  Result     := '';
  WasRunning := AppIsRunning();
  if WasRunning then
    ShellExec('runas', ExpandConstant('{cmd}'), '/C taskkill /F /IM "{#AppExe}"',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // taskkill returns once termination is requested, and the UAC prompt above can be declined
  // outright. Confirm the process is actually gone: with nothing left to unlock the install can
  // proceed, otherwise offer a retry, so the application can be exited from its own icon and the
  // installation carried on in the same run rather than started over. Only when that is declined
  // does the terminal message stop Setup, rather than failing mid-copy on a locked exe.
  Result := ConfirmProcessHasExited('{#AppExe}');

  // Setup stops here on a non-empty result, and in an unattended run it stops showing nothing at
  // all. Leave the reason on disk where the next start reads it, so the one failure this flow can
  // have is stated to somebody rather than only counted in an exit code nothing is left to read.
  if (Result <> '') and StartedByTheApplication() then RecordTheRefusal();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('runstartup') then RegisterStartupTask();
    if not WizardSilent() then
      // Interactive install: launch after task creation so a freshly-created startup task
      // is used for a prompt-free launch.
      LaunchApp()
    else if StartedByTheApplication() then
      // The application asked for this update and closed itself for it, so it is put back — and
      // unconditionally, unlike the branch below. WasRunning is False here by design (the exit is
      // waited for in PrepareToInstall rather than forced), and gating on the AutoStart task would
      // answer a user's own Update with the application simply gone. LaunchApp prefers that task
      // where it exists and otherwise starts the application directly, which costs at most the one
      // consent prompt the application always needs — and none at all when Setup inherited the
      // elevated token of the application that started it.
      LaunchApp()
    else if WasRunning and ScheduledTaskExists() then
      // Silent upgrade that killed a running instance: restart it via the elevated logon task — no
      // UI, no UAC. Without this the background upgrade leaves the tray app dead until the next
      // sign-in. When no task exists we stay silent (a UAC prompt from an unattended install would
      // be wrong) and accept the gap.
      Exec('schtasks.exe', '/Run /TN "' + TaskName + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // usUninstall fires just BEFORE files are removed — stop the app first so its files
  // aren't locked, otherwise the uninstall leaves the exe behind and the app keeps running.
  if CurUninstallStep = usUninstall then
    // Elevate once only if there's something elevated to do (app running or a HIGHEST task).
    if AppIsRunning() or ScheduledTaskExists() or WatchdogTaskExists() then
      StopAppAndRemoveStartupTask();
end;
