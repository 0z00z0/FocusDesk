# FocusDesk release notes

The one source for what a release changed. The release workflow publishes the section for the tag it
is building as that release's body, and the About page's "What's new" opens that release's page, so
the application's report and the release cannot say different things.

**One sentence per change**, saying what is better for someone using the application, not what moved
in the code. Newest version first; the heading is the version alone, exactly as it appears in
`FocusDesk.csproj`. A section never carries an installer hash: the workflow adds the one hash the
updater reads.

## 1.0.0

- FocusDesk holds the machine in a chosen state for a chosen length of time, started from its icon in
  the notification area.
- A session takes up to four levers: blocking every network connection except the MQTT broker and a
  named list of programs, dimming the screen, covering every display with a black window, and
  blocking the mouse and keyboard.
- A session ends when its time runs out, or early from Home Assistant: a first cancel starts a wait,
  and a second one inside the short window that follows ends the session. The machine itself offers
  no way to end one; while the mouse and keyboard are blocked, Ctrl+Alt+Delete and signing out remain
  the way out.
- The session, its levers, its state and its remaining time appear in Home Assistant as one device,
  and a session can be started from there.
- The pop-out from the notification-area icon shows the running session and the most recent ones,
  and every finished session is kept in a history file.
- Settings has six pages: Focus, Screen, MQTT, Appearance, About and App diagnostics.
- An update is checked for on request, from the menu or the About page, and only a release signed by
  ZeroZero Software is installed.
- The application needs administrator rights, so each start asks for consent once, unless it starts
  at sign-in through the logon task the installer offers.
