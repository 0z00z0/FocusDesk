<!-- lang: en-GB -->
# FocusDesk

A Windows tray application that holds the machine in a chosen state for a chosen length of time.

A focus session runs for one duration and is one of two kinds, chosen when it starts:

- **Program focus.** Runs on one of up to five named sessions. Only the programs the session ticks
  "Can run" can be worked in; every other program's window is minimised.
- **Screen break.** Every display is covered with a black window. The mouse and keyboard can be
  blocked and the screen dimmed as well, and every network connection can be blocked except the MQTT
  broker and the programs the named session in use allows the network.

A session ends when its time runs out, or early from Home Assistant through a staged cancel. The
machine itself offers no way to end one. FocusDesk does not defend against a determined
administrator, never closes a program, and does not block per site.

## Install

Download `FocusDesk-Setup-<version>.exe` from the latest release and run it. It installs for the
current user only and needs no administrator rights to install. The application itself needs them,
so each start asks for consent once, unless it starts at sign-in through the logon task the
installer offers.

Releases are signed by `CN=ZeroZero Software`, and the application installs an update only when it
carries that signature. Updates are checked for from the menu or the About page. A switch on the
About page, off until turned on, lets them install by themselves while the screen is locked or the
machine has been left alone for ten minutes. No update installs during a focus session, whether it
was started by hand or by itself.

## Home Assistant

FocusDesk connects to an MQTT broker and appears in Home Assistant as one device carrying the
session switch, its length, the kind of session it starts, a switch per optional lever, the
session's state, its remaining time and the screen brightness. The broker is set on the MQTT page in
Settings. Each entity carries a one-sentence help text, in English, which a dashboard shows under the
entity's name with `secondary_info: description` on an entities card or `state_content: description`
on a tile card.

The kind is the select "Focus session type", with the options "Program focus" and "Screen break" in
English whatever language FocusDesk runs in. The select "Named session" picks the named session
Program focus runs on, and refuses a change while a session runs. Starting a Screen break is two
calls in order: set "Focus session type", then turn the session switch on. Starting Program focus
sets "Named session" between the two.

## Build

Building needs the .NET 10 SDK and read access to the ZeroZero package feed. The installer and the
release process are described in `installer\README.md`; a release is made only by pushing a
`v*.*.*` tag.

## Licence

MIT, see `LICENSE`.
