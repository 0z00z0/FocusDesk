# FocusDesk release notes

The one source for what a release changed. The release workflow publishes the section for the tag it
is building as that release's body, and the About page's "What's new" opens that release's page, so
the application's report and the release cannot say different things.

**One sentence per change**, saying what is better for someone using the application, not what moved
in the code. Newest version first; the heading is the version alone, exactly as it appears in
`FocusDesk.csproj`. A section never carries an installer hash: the workflow adds the one hash the
updater reads.

## 0.5.2

- Started at sign-in, FocusDesk waits for the taskbar before putting its icon in the notification
  area, and closes if the icon still cannot be placed, so a later start works.

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
  broker and the programs allowed the network; Program focus can add the network block, and Screen
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
