<!-- lang: en-GB -->
# Installer and distribution

FocusDesk ships as a per-user Inno Setup installer (`%LocalAppData%\Programs`, no administrator
rights to install), published as an asset on each GitHub release, with the winget manifests
attached to the same release beside it. The package is in no winget source, so the identifier
resolves by name nowhere; downloading the installer asset is the install route. The application
itself is elevated at runtime; the installer is not.

## Build the installer

One-time prerequisite: Inno Setup 6, `winget install JRSoftware.InnoSetup`.

Then, from Windows PowerShell and never with stderr merged into the pipeline:

    cd installer
    .\build-installer.ps1 -Version 1.3.0

The script publishes the application self-contained (win-x64, no trimming), signs the published
executable and the installer where a certificate is present, and compiles `FocusDesk.iss` into
`installer\Output\FocusDesk-Setup-<version>.exe`. Called without `-Version` it auto-bumps the patch
component and rewrites the project file, so a release build always states its version.

The filename always carries the version: `FocusDesk.iss` sets
`OutputBaseFilename=FocusDesk-Setup-{#AppVersion}` and the build script passes the version in as
`/DAppVersion`. Earlier `FocusDesk-Setup-*.exe` files are deleted from `Output` before compiling, so
the folder holds exactly one installer. The script then prints the installer's SHA256, computed
after signing; a tagged release does not need that value, because the workflow computes the hash
itself and patches the manifests with it.

## What the installer does

- Installs per-user to `%LocalAppData%\Programs\FocusDesk`, with no administrator prompt.
- Adds a Start-menu entry, and a desktop shortcut where that box is ticked.
- Offers "Run at startup": a `RunLevel=Highest` logon task named `FocusDesk AutoStart`, so the
  elevated application starts at sign-in with no boot-time consent prompt. Creating that task is
  the only step that elevates, and only when the box is ticked. The application's own tray toggle
  manages the same task.
- Stops a running instance before replacing files, through an elevated `taskkill`, and offers a
  retry rather than failing mid-copy on a locked executable.

## Artwork

- `SetupIconFile` is `Assets\FocusDesk.ico`, the product mark the executable carries, drawn by
  `scripts\build-icons.ps1`.
- `WizardImageFile` and `WizardSmallImageFile` are the two bitmaps in `wizard\`, drawn by
  `scripts\build-wizard-images.ps1`: the side banner holds the studio mark above the product mark,
  and the inner-page header holds the product mark alone on white.
- Both scripts draw the mark from `scripts\FocusDeskMark.ps1`, and their output is committed, so no
  build runs either. Run them only when the artwork changes.

## Releasing

`.github/workflows/release.yml` builds, signs and publishes everything on a `v*.*.*` tag push. It is
the only way a release is created; a hand-made release carries neither the patched manifests nor
the assertion that the manifests describe the build.

The repository secret `CODE_SIGN_PFX` holds the certificate: the base64-encoded studio PFX,
subject `CN=ZeroZero Software`, which is the signer the update check accepts. The studio
certificate has an empty password, and GitHub refuses an empty secret, so `CODE_SIGN_PASSWORD` is
intentionally absent; the signing step passes a password only where that secret holds one. With
`CODE_SIGN_PFX` absent the signing step is skipped, and a tag push then fails outright rather than publishing an unsigned installer; a
manual dispatch only warns, so a dry run stays useful.

To cut a release: bump `<Version>` in `FocusDesk.csproj`, which is the single source and what the
tag must match, add a section headed with that version alone to `RELEASE-NOTES.md`, then push a
`v*.*.*` tag. A section the file does not hold leaves the release body to the commit subjects. The workflow publishes the application, compiles the
installer, signs both executables, computes the SHA256, patches the winget manifests in its own
working copy, creates the release with the installer and the three manifests attached, and runs
`winget validate` against them.

## winget manifests

`installer\winget\` holds the three manifests the release workflow patches before attaching them:

    0z00z0.FocusDesk.yaml
    0z00z0.FocusDesk.installer.yaml
    0z00z0.FocusDesk.locale.en-GB.yaml

It is the maintainer's source for those files, not an install route: the version, URL and hash
committed there describe whichever build they were last edited for. Validate a change before
committing it with `winget validate --manifest installer\winget`.

Installing from the manifests attached to a release works today, after
`winget settings --enable LocalManifestFiles` once: download all three into one folder and run
`winget install --manifest <folder>`. Downloading the installer asset and running it is simpler and
needs no winget at all.
