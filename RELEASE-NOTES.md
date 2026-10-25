# FocusDesk release notes

The one source for what a release changed. The release workflow publishes the section for the tag it
is building as that release's body, and the About page's "What's new" opens that release's page, so
the application's report and the release cannot say different things.

**One sentence per change**, saying what is better for someone using the application, not what moved
in the code. Newest version first; the heading is the version alone, exactly as it appears in
`FocusDesk.csproj`. A section never carries an installer hash: the workflow adds the one hash the
updater reads.

## 1.1.0

- Program focus runs on a named session, each with its own list of allowed programs, and up to five
  named sessions can be kept.
- The Program focus page in Settings lists the named sessions: Activate makes one the session in use,
  marked In use, and sessions are renamed, added and deleted there.
- On the dashboard opened from the notification-area icon, choosing Program focus shows one button
  per named session to pick the one it runs on; Screen break keeps the goal field in that place.
- The MQTT select "Named session" picks the named session a Program focus session uses, and refuses a
  change while a session runs.
- Settings has seven pages: Focus session, Program focus, Screen break, MQTT, Appearance, About and
  Diagnostics.
- The dashboard's Restrictions card and Recent sessions rows use short wording, and every text in them
  follows the interface language, Norwegian included.
- Program focus never blocks the network. The network block belongs to Screen break, set on the
  Screen break page or with the MQTT switch "Focus session blocks network", and lets through the MQTT
  server and the programs ticked Can use the network in the named session in use.
- The existing program list carries over unchanged into a named session called "Default" ("Standard"
  in a Norwegian interface).

## 1.0.0

- FocusDesk is a Windows tray application that holds the computer in a chosen state for a chosen
  length of time, in one of two kinds of focus session: Program focus, where only the programs ticked
  Can run can be used and every other window is minimised, and Screen break, where every display is
  covered.
- A session has five levers: the program limit, the screen cover, the mouse and keyboard block,
  dimming of the built-in screen, and a network block that lets through only the MQTT server and the
  programs allowed the network. Program focus can add the network block; Screen break can add the
  input block, the dimming and the network block.
- Before a session starts from the pop-out, the full-screen focus point window runs a short breathing
  exercise. The Focus page sets whether it appears, how long it runs and whether the session then
  starts by itself; closing it early starts nothing.
- A session ends when its time runs out, or early by an MQTT command after a five-minute wait and a
  second request. Nothing on the computer ends one.
- Over MQTT, FocusDesk appears as one device carrying the session switch, its length, its kind, a
  switch per optional lever, the session's state, its remaining time and the screen brightness. Every
  entity carries a one-sentence help text, which a Home Assistant dashboard can show under its name.
- The Automatic session page in Settings opens a step-by-step guide to starting a focus session at a
  set time from a Home Assistant automation.
- While a session runs, the tray menu's Update available, Settings, Launch at startup, Check for
  updates, About and Exit rows are greyed out. So are the Settings controls that could weaken it: the
  brightness slider and its restore button, the update button and the settings-file actions on the
  Diagnostics page.
- No update installs while a session runs, whether it was chosen from the menu or the About page or
  arrived through automatic updates. Only a release signed by ZeroZero Software is installed.
- An automation or dashboard built on 0.4.3 or earlier that used the switches Focus session covers
  screen or Focus session limits programs has to be pointed at the select Focus session type instead.

## 0.5.2

- Started at sign-in, FocusDesk waits for the taskbar before putting its icon in the notification
  area. If the icon still cannot be placed, or the start fails in any other way, FocusDesk closes
  so a later start works.
- The screen cover and the pop-out window still open after Explorer has restarted and no taskbar
  exists; the log notes it once instead of the cover failing to appear.

## 0.5.1

- A click anywhere on the full-screen focus point no longer cancels the session about to start, so a
  stray click cannot undo it; Escape or Alt+F4 cancels it.
- A quotation from Andrew Huberman, in English in every language, stands under the focus point for the
  whole breathing exercise.
- On a computer with no display whose brightness Windows can set, the log no longer records an error
  about screen brightness; it notes once, as information, that no such display is present.

## 0.5.0

- FocusDesk holds the computer in a chosen state for a chosen length of time, in one of two kinds of
  session: Program focus, where only the programs ticked Can run in a list picked from the Start menu
  can be used and every other window is minimised, or Screen break, where every display is covered.
- A session can do five things: limit work to the chosen programs, cover every display, block the
  mouse and keyboard, dim the built-in screen, and block every network connection except the MQTT
  server and the programs allowed the network; Program focus can add the network block, and Screen
  break can add the mouse and keyboard block, the dimming and the network block.
- The Start button in the pop-out from the notification-area icon starts either kind, with an
  optional goal that takes the place of the standard text on the focus point and the screen cover and
  is never sent to Home Assistant or written to the history.
- Before a session starts from the pop-out, a full-screen focus point runs a short breathing
  exercise; the Focus page sets whether it appears, how many seconds it runs and whether the session
  then starts by itself, and closing it early starts nothing.
- The screen cover shows a ring that counts the session down and reads the last minute second by
  second.
- A session ends when its time runs out, or early from Home Assistant after a five-minute wait and a
  second request; nothing on the computer ends one, and while the mouse and keyboard are blocked,
  Ctrl+Alt+Delete still reaches Windows' own screen to sign out or restart.
- Exit is greyed out in the menu while a session runs, and an update chosen during a session does not
  install until it is chosen again after the session ends.
- The session, its kind, length, settings, state and remaining time appear in Home Assistant over
  MQTT as one device, from which a session of the chosen kind can be started.
- The pop-out shows the running session and the most recent ones, and every finished session is kept
  in a history file.
- Settings has seven pages, Focus, Automatic session, Screen, MQTT, Appearance, About and
  Diagnostics, and the Automatic session page opens a step-by-step guide to starting a session at a
  set time from Home Assistant.
- An update is checked for on request, from the menu or the About page, and only a release signed by
  ZeroZero Software is installed.
- A switch on the About page, off until turned on, lets updates install by themselves: checked once a
  day and installed while the screen is locked or nothing has been touched for ten minutes, never
  during a focus session.
- The application needs administrator rights, so each start asks for consent once, unless it starts
  at sign-in through the logon task the installer offers.
