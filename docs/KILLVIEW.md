# Head icon and kill card switches (visual scope)

At runtime two independent switches replace the old selector, but `KillIcon/Display`
remains bound and hot-reloadable for users who have not opted into the new switches.
The raw cfg file is snapshotted before any new key is bound, so BepInEx defaults cannot
hide whether the user already had new keys or a legacy value. `KillIcon/ViewMode` is an
internal migration marker; it is not a user-facing feature switch.

| Key | Default | Meaning |
|---|---|---|
| `Card/Enabled` | `true` | kill card |
| `KillIcon/HeadEnabled` | `false` | head icon |
| `KillIcon/Opacity` | `0.5` | head icon + flight badge opacity, 0–1, non-finite → 0.5 |
| `KillIcon/ViewMode` | empty | internal: `Legacy:Head`, `Legacy:Card` or `Explicit` |

Both switches are evaluated independently; enabling both draws both. `General/Enabled`
and the F7 toggle still gate the whole HUD, and F9 previews only the enabled effects.

## Legacy migration and priority

- `KillIcon/Display = Head` (or any unrecognised value, matching the old parser)
  means head on, card off.
- `KillIcon/Display = Card` or `Cards` means head off, card on.
- A legacy file with neither new key starts in `Legacy:<kind>` mode and keeps
  following `Display` on every `ConfigFile.Reload`. Head→Card→Head therefore keeps
  working without restarting the game.
- If `ViewMode` is `Legacy:<kind>` and the two new keys still equal that legacy
  mapping, `Display` is authoritative; the new keys are re-synced in memory on hot
  reload and persisted at the next normal load.
- As soon as either new key differs from the synced legacy mapping, the file becomes
  `Explicit`. The two new keys are then independent, `Display` is ignored, and the
  missing key (if any) is frozen from the last synced legacy mapping so later
  `Display` edits cannot change it. The marker and missing key are written at the
  next normal load; until then the same state is re-derived from the synced marker,
  so a restart during that window is still stable.
- A file with no legacy key and no new keys starts `Explicit` with card on / head off.
- If `ViewMode` is `Explicit`, the new keys are authoritative even when `Display`
  exists. Deleting the marker re-enters the auto-detection rules above.
- Existing values of other keys are not rewritten by this plugin. BepInEx itself may
  normalise an invalid typed value when it binds that key.

## Opacity

`KillViewPolicy.CompositeAlpha` multiplies the lifecycle fade by the configured
opacity exactly once; the result is passed to both the icon texture and
`DrawFlightMark`. There is no second opacity multiply in the draw path.

## Hitbox avoidance

`KillIconOccluders` keeps a bounded registry of live `EnemyAgent` instances
(`KillOccluderRegistry<T>` + `KillOccluderRecovery`, both Unity-free). It is seeded once
per level (or first use) by a single `FindObjectsOfType<EnemyAgent>()` scan, then updated
by the existing `EnemyAgent.Setup` / `OnDeSpawn` patches. Per frame it never scans the
scene; it walks the capped registry (96 enemies, 48 colliders per enemy) and unions each
enemy's active collider bounds. A candidate is projected to screen space and rejected
when it is behind the camera, behind the near clip plane or fully offscreen. Non-enemy
colliders are never candidates, so they cannot fill a buffer and hide an enemy.

Two kinds of incompleteness are kept apart:

- *This frame only.* One capture exception, a collider count above 48 or a saturating
  candidate buffer hides every head icon for that frame only. The next frame captures
  again, so a transient fault cannot outlive the frame; the registry is untouched.
- *Registry incomplete.* If the seed scan or `Register` hits the 96-enemy cap, a live
  enemy is known to be missing and icons stay hidden (fail-closed) until a bounded rebuild
  proves that every live enemy fits again. Rebuilds are rate limited — first attempt 0.5 s
  after the overflow, then 1 s, 2 s, 2 s … while the cap is still exceeded — and each is a
  single `FindObjectsOfType` scan: never per frame, never per icon. When a scan sees all
  live enemies fit, the registry is complete again and icons reappear without a level
  change or restart. While the live count still exceeds the cap the icons stay hidden and
  the next attempt is scheduled.

A capture that keeps failing is retried on the next two frames, then backs off
(0.25 s, 0.5 s, 1 s, 2 s cap) instead of enumerating colliders every frame; the icons stay
hidden while that enemy cannot be measured, and reappear as soon as a retry succeeds.
`Reset` (tracker reset) and `EnterLevel` clear the registry, the seeded flag and all
recovery timers. Nothing here writes game state, and the card is still governed only by
`Card/Enabled`.

`KillViewPolicy.ResolvePlacement` tests the **full visual box** (icon plus flight
badge when present) against the candidate screen boxes. It lifts the box above the
highest overlapping top edge with a 2 px margin and repeats up to four times so a
higher box encountered after the first move is handled; if it still overlaps, leaves
the screen top, or the candidate set is incomplete, the icon is hidden. Boxes that do
not overlap the current visual box do not affect the position. No game state is
written.

## Verification limits

Camera projection, collider lifetime/destruction and actual on-screen results cannot
be executed in this repository. See `tests/README.md`; the IL guard only proves the
call wiring in the built assembly.
