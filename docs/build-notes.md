<!-- lang: en-GB -->
# Build and logging reasoning

Long-form reasoning for comments across FocusDesk's source that would otherwise run past the
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

## nlog.config

Four traps carried over from a sibling project's own concurrent-writer regression, unchanged
because the same shape of failure — several FocusDesk processes appending to one file — applies
here too.

**keepFileOpen.** NLog's default, `keepFileOpen="true"`, holds an exclusive handle; a sibling
process's concurrent writes are then silently lost, not even reported to NLog's own internal log.
`keepFileOpen="false"` opens the file per write with a share mode that tolerates other writers,
paired with the `RetryingWrapper` to ride out a collision rather than drop the line.

**concurrentWrites.** Do not restore `concurrentWrites="true"`. That was NLog 5's name for this
behaviour; NLog 6 removed the property, and an unrecognised attribute is silently ignored unless
`throwConfigExceptions` is on — so it would look correct, do nothing, and lose lines exactly as the
default `keepFileOpen` does.

**Archive counting.** Do not swap `maxArchiveFiles` for `maxArchiveDays`. Measured: `maxArchiveDays`
judges an archive by its creation time, and Windows preserves creation time when the active file is
moved to its archive name. A log file created weeks ago is archived and deleted in the same write,
so an upgrade destroys the existing `app.log`, and a week of downtime destroys that week.
`maxArchiveFiles` counts archives instead and survives both. NLog 6 also renamed the surrounding
area: `archiveNumbering` and `archiveDateFormat` are obsolete from 6.1.4, replaced by
`archiveSuffixFormat` — a config written in NLog 5's idiom parses and rotates nothing.

**Layout.** No trailing newline: `lineEnding="LF"` already terminates the entry, and carrying both
would put a blank line between every pair of entries. The class column comes from an event property
the `AppLog` facade fills from `[CallerFilePath]` — free, and correct across async boundaries, where
`${callsite}` costs 2.2x per entry and reports the facade instead of the caller. The timestamp
carries milliseconds so ordering inside one second stays resolvable.

**Date culture.** `${date}` has no `culture=` parameter because it already defaults to
`InvariantCulture`, and this timestamp is machine-facing log data that must stay Gregorian/ASCII on
every locale. An empty `culture=""` is not the fix: it makes NLog fall back to the thread's
`CurrentCulture`, which stamps a non-Gregorian year under some locales (measured: 1448-02-03 under
ar-SA on the sibling project this config is drawn from). `culture=Invariant` is not a thing and
throws.

## AppLog.cs

**Class column via `[CallerFilePath]`.** Supplied by the compiler, so it costs nothing at run time
and survives async boundaries. `${callsite}` would report this facade instead of the true caller,
and measured 2.2x the per-entry cost when made to do so.

**`Initialise` must never throw.** It runs from the `_log` field initialiser, so anything thrown
here escapes as a `TypeInitializationException` at whichever call site touches `AppLog` first —
several of which are startup and crash paths.

**Failed config load.** Reading `LogManager.Configuration` triggers NLog's auto-discovery of
`nlog.config` beside the exe. A missing or unparseable file — the latter is a user-editable one, and
a bad hand-edit must not be what takes the app down — leaves it null or unset, and NLog then logs
nothing at all for any logger this returns. That silence is the whole of the degradation: nothing
here builds a second copy of the configuration to fall back to.

## SafeFileAppend.cs

`FileMode.Append` + `FileShare.ReadWrite` is what lets concurrent FocusDesk processes share a file
for write: the handle uses `FILE_APPEND_DATA`, so every write lands at the current end of file
whatever another handle is doing, and lines can neither clobber nor tear each other.
`File.AppendAllText` cannot be used — its default `FileShare.Read` denies concurrent writers.

Only sharing and lock collisions are retried; a missing directory, access denial or a path that is
too long fails fast. `Append` rethrows the final failure, `TryAppend` reports it as a bool.
