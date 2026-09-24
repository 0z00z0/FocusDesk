<!-- lang: en-GB -->
# FocusDesk.csproj — build reasoning

Long-form reasoning for comments in `FocusDesk.csproj` that would otherwise run past the
one-or-two-line cap. Each heading matches the short in-project comment that points here.

## Compile and None glob removal

`Tests\` and `publish\` both sit under this project's directory, so the SDK's default item globs
claim them. `Tests\` holds the separate `FocusDesk.Tests` project; `publish\` is this project's own
publish output (`installer\build-installer.ps1` publishes into `<repo>\publish`, which ISCC then
packs), gitignored and therefore invisible in review.

Measured with the removes stripped and both trees populated: the `Compile` glob claims 25 files out
of `Tests\` — xUnit-only source that would compile into this assembly too and fail — and the `None`
glob claims 1 090 out of `Tests\` and 2 700 out of `publish\`, nearly all Windows App SDK `.mui`
satellites. The `Content`, `Page`, `EmbeddedResource` and `ApplicationDefinition` globs claim
nothing out of either tree today, so they carry no remove.

Two conditions bring those back. An `Assets` folder in this project makes the `Content` glob live,
and the WinUI targets copy a `Content` item regardless of `CopyToOutputDirectory` — that metadata
governs the plain SDK's copy, not theirs — so the copy nests one level deeper per build until
`MAX_PATH` ends it (`MSB3021`). A referenced WinUI library shipping loose `.xaml` into either tree
makes the `Page` glob live, and the XAML compiler then emits a second `*.g.i.cs` for a type that is
already referenced (`CS0436`). Both are live in ChargeKeeper; neither is here yet.

## SignOutput's ContinueOnError

`ContinueOnError="true"` demotes any signing failure to a warning, not only "no certificate
present" — an expired certificate, a locked key or a rejected timestamp are swallowed the same way,
and the build reports success regardless of which one occurred.

That is accepted here because it is not the only gate: `installer\build-installer.ps1`'s own later
signing calls (publish output, then the compiled installer) check `$LASTEXITCODE` and throw on a
non-zero exit, so a real signing failure still stops a release before it ships. `SignOutput` exists
to sign the ordinary `bin\` build output for local development (so it never shows *Unknown
Publisher*, which matters given `requireAdministrator`), not to gate anything.

The kit's own `ZeroZero.Build` package ships a publish-time signing target that signs only when
`ZeroZeroSignThumbprint` or `ZeroZeroSignPfx` is set and fails the publish outright on a real
signing error, with no `ContinueOnError` needed — absence of a certificate property is itself the
"nothing to do" case. `scripts\sign.ps1` and the `SignOutput` target predate that target and
duplicate part of what it does; adopting it instead is a deliberate migration, not part of this
cleanup.

## PrunePublishOutput

`Microsoft.WindowsAppSDK` pulls in `Microsoft.Windows.AI.MachineLearning` as a transitive
dependency, which adds `onnxruntime.dll` (20 MB), `DirectML.dll` (18 MB) and friends to the publish
output. Nothing in FocusDesk calls a `Windows.AI` API; the libraries load on demand only, so
removing them after publish is safe and saves about 38 MB from the installer.
