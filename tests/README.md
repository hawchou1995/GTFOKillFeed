# KillFeed visual tests

These tests exercise the production sources directly; they do not copy the policy
into the test project.

- `KillFeed.Policy.Tests` compiles `src/HawChow.GTFOKillFeed/KillViewPolicy.cs`,
  `KillOccluderRecovery.cs` and `KillViewConfigApply.cs` by link, so a regression in the
  shipped policy fails the tests. It also drives a real
  `BepInEx.Configuration.ConfigFile`: bind, raw-edit, `Reload`, and restart paths cover
  legacy Head→Card→Head, upgrade restart, one new key, both on, and old/new conflicts.
- `OccluderRecoveryTests` drive the shipped `KillOccluderRecovery` and
  `KillOccluderRegistry<T>` through `OccluderFrameDriver`, which replays
  `KillIconOccluders.Collect` in the same order (seed/rate-limited rebuild, prune, capture
  with retry backoff, bounded candidate append). Covered: a first capture failure recovers
  on the next frame; a saturating candidate buffer hides one frame and then recovers; a
  49-collider enemy recovers when the count drops; the 97th enemy marks the registry
  incomplete and icons stay hidden while 96+ enemies are alive, then a bounded rescan
  recovers once the live count drops; sustained capture failures back off instead of
  running every frame and recover within the backoff cap; a failed seed retries rate
  limited; `Reset`/level enter clear registry, seeded flag, frame flag and timers. The
  negative control (`LegacyStickyModel`) replays the 0f72b86 rule that one fault hides the
  icons for the rest of the level and asserts it never recovers, so the recovery
  assertions cannot pass against the old sticky behaviour.
- `KillFeed.IlGuard` reads the built `GTFOKillFeed.dll` with Mono.Cecil and checks
  the wiring that unit tests cannot see: snapshot-before-bind order, config defaults,
  one opacity multiply, one candidate collection per frame, the one-time registry
  seed, spawn/despawn registration, independent card/head gates, F7/F9 and
  General/Enabled reads, no game-state setters and no per-frame scene scan. It also
  checks the recovery wiring: exactly two `FindObjectsOfType` call sites (one-time seed
  plus the rebuild that only runs after the recovery limiter says it is due), the
  frame-local hide call sites, `NoteRegistryRebuilt` only from a successful rebuild,
  `NoteRescanFailed` on every failed rebuild, `EnterLevel` going through `Reset`, and no
  `_complete` field left on `KillIconOccluders`. It also
  asserts that `HitMarkerHud.Submit` calls the pure `HitMarkerPolicy.AcceptsSubmission`
  gate (disabled channel or `HitMarkerKind.None` -> reject) before projection/queue work,
  and that the rejected path returns `false` immediately.
- `GTFOKillFeed.Smoke` (authoring 1.2.21 suite, 1409 assertions) links the same policy sources
  and embeds the shipped `KillFeed.Audio.*.wav`, `KillFeed.Icons.hitmark-*.png` and
  `KillFeed.EnemyNames.defaults.json`, so cue bytes, marker rasters and the name list are
  asserted against the files the plugin actually ships. It also carries `KillCounterTests`
  (tier totals, saturation, layout, `CorpseFadeRetry` windows, plus two source-level gates:
  exactly one `Counters.Add(` call site and no F9-preview credit) and the 1.2.19/1.2.20
  shot-volley and corpse suites. Its host walks up to `src/GTFOKillFeed.csproj` and then reads
  source text from `src/HawChow.GTFOKillFeed`.
- `GTFOKillFeed.ConfigSmoke` (97 assertions) drives the real `BepInEx.Configuration.ConfigFile`
  through fresh-install, legacy-upgrade, partial-new-key and unreadable-file paths; it needs
  `GTFO_DIR`.
- `GTFOKillFeed.HudReplay` (20 assertions) runs the production `KillCounterHud.Tick`/`Draw`
  against `EngineStubs.cs`, a double of the Unity/Il2Cpp API surface only (no gameplay): three
  tiers, every "compositor not verifiable" refusal, a target/window grid mismatch and a missing
  sprite; it also asserts the native health bar is never moved or rescaled.

The SDK is pinned by the repository root `global.json` (6.0.428, `rollForward=latestPatch`);
a host without a matching 6.0.x SDK fails with `A compatible .NET SDK was not found` instead
of silently building different IL.

Commands (from the repository root):

```bash
DOTNET=/path/to/dotnet
GTFO_DIR=/path/to/Survival

$DOTNET build src/GTFOKillFeed.csproj -c Release -p:GTFO_DIR="$GTFO_DIR" --configfile nuget.config
$DOTNET build tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
$DOTNET run -c Release --no-build --project tests/KillFeed.Policy.Tests/KillFeed.Policy.Tests.csproj
$DOTNET build tests/KillFeed.Tests/KillFeed.Tests.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
$DOTNET tests/KillFeed.Tests/bin/Release/net6.0/KillFeed.Tests.dll
$DOTNET build tests/KillFeed.IlGuard/KillFeed.IlGuard.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
$DOTNET tests/KillFeed.IlGuard/bin/Release/net6.0/KillFeed.IlGuard.dll src/bin/Release/GTFOKillFeed.dll
$DOTNET build tests/GTFOKillFeed.Smoke/GTFOKillFeed.Smoke.csproj -c Release
$DOTNET tests/GTFOKillFeed.Smoke/bin/Release/net6.0/GTFOKillFeed.Smoke.dll
$DOTNET build tests/GTFOKillFeed.ConfigSmoke/GTFOKillFeed.ConfigSmoke.csproj -c Release -p:GTFO_DIR="$GTFO_DIR"
$DOTNET tests/GTFOKillFeed.ConfigSmoke/bin/Release/net6.0/GTFOKillFeed.ConfigSmoke.dll
$DOTNET build tests/GTFOKillFeed.HudReplay/GTFOKillFeed.HudReplay.csproj -c Release
$DOTNET tests/GTFOKillFeed.HudReplay/bin/Release/net6.0/GTFOKillFeed.HudReplay.dll
```

Expected: policy tests print `RESULT pass=424 fail=0`, `KillFeed.Tests` prints
`SUMMARY pass=407;fail=0`, the three authoring suites print `PASS: 1409 kill-feed policy
assertions`, `CONFIG_SMOKE=PASS;ASSERTIONS=97` and
`HUD_REPLAY=PASS;ASSERTIONS=20;PRODUCTION=Tick+Draw`, and the IL guard prints
`ILGUARD failures=0`; all exit 0.
The documentation gate is separate: `python tools/verify_docs.py` must print
`DOCGATE RESULT gates=16 failures=0`, and `python tools/verify_docs.py --negative` must
report that all 7 negative controls raised their expected gate.

Not executable here: Unity camera projection, live `EnemyAgent`/`Collider` lifetime,
`FindObjectsOfType` results, GUI rendering, and in-game verification. The IL guard only
proves the calls and their order exist in the built assembly; it does not prove runtime
visuals. Candidate capacity/saturation, cross-depth screen overlap, iterative
re-avoidance, badge-area and occluder-recovery tests are pure policy tests: the recovery
driver replaces the scene scan with a list and collider bounds with one fixed screen box,
so it proves the shipped decisions, not Unity's actual scan/collider results. The IL guard
was also run against two mutated copies of the built DLL (renamed `RescanDue` and
`NoteRegistryRebuilt`) and failed both times, which is the negative control for the new
assertions.
