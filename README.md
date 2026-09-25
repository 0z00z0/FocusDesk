<!-- lang: en-GB -->
# FocusDesk

A Windows tray application that holds the machine in a chosen state for a chosen length of time.

A focus session runs for one duration and takes up to four levers:

- Block every network connection except the MQTT broker and a named list of programs.
- Dim the screen.
- Cover every display with a black window.
- Block the mouse and keyboard.

A session ends when its time runs out, or early from Home Assistant through a staged cancel. The
machine itself offers no way to end one. FocusDesk does not defend against a determined
administrator, and it does not block per application or per site.

## Install

Download `FocusDesk-Setup-<version>.exe` from the latest release and run it. It installs for the
current user only and needs no administrator rights to install. The application itself needs them,
so each start asks for consent once, unless it starts at sign-in through the logon task the
installer offers.

Releases are signed by `CN=ZeroZero Software`, and the application installs an update only when it
carries that signature.

## Home Assistant

FocusDesk connects to an MQTT broker and appears in Home Assistant as one device carrying the
session switch, its length, a switch per lever, the session's state, its remaining time and the
screen brightness. The broker is set on the MQTT page in Settings.

## Build

Building needs the .NET 10 SDK and read access to the ZeroZero package feed. The installer and the
release process are described in `installer\README.md`; a release is made only by pushing a
`v*.*.*` tag.

## Licence

MIT, see `LICENSE`.
