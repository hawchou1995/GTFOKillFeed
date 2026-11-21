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
- `GTFOKillFeed.Smoke` (1542 assertions in 1.2.24) links the same policy sources
  and embeds the shipped `KillFeed.Audio.*.wav`, `KillFeed.Icons.hitmark-*.png` and
  `KillFeed.EnemyNames.defaults.json`, so cue bytes, marker rasters and the name list are
  asserted against the files the plugin actually ships. It also carries `KillCounterTests`
  (tier totals, saturation, layout, `CorpseFadeRetry` windows, plus two source-level gates:
  exactly one `Counters.Add(` call site and no F9-preview credit) and the 1.2.19/1.2.20
  shot-volley and corpse suites. Its host walks up to `src/GTFOKillFeed.csproj` and then reads
  source text from `src/HawChow.GTFOKillFeed`.
  It also tests fade-input refusal, the stagger row, stagger attribution in a pellet batch,
  and the production paint passes for partially green numbers.
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

The [reproduction guide](../docs/REPRODUCE.md) lists the build commands, expected results
for the five test suites and `KillFeed.IlGuard`, and invocations of the nine PowerShell IL
checks against the same rebuilt DLL. Use a read-only `GTFO_DIR` and an external scratch
directory. The older `verify_docs.py` checks belong to the historical 1.2.17 layout.

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
