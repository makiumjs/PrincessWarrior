## Round 1 — 2026-09-04

**Correctness/robustness: 6/10**

What's implemented is implemented correctly. `OnCheckpointReached`/
`OnAbilityUnlocked` (SaveManager.cs:75-85) do exactly what the EventBus
contract says — set the field, call `Persist()`. `Persist()` (line 87-94)
checks `ResourceSaver.Save`'s return `Error` and logs via `GD.PushError`
rather than throwing. No race condition: Godot C# signals invoke handlers
synchronously on the main thread, so a same-frame checkpoint+unlock is two
sequential calls, not concurrent ones — worst case is two full disk writes
for one logical update (minor inefficiency, not a bug). `_ExitTree`
unsubscribe (lines 41-53) is present but moot — `SaveManager` is itself an
autoload with the same lifetime as `EventBus`, so it only runs at process
shutdown; verified there's no scenario where `SaveManager` is torn down
while `EventBus` survives.

The corrupt-file path (`LoadSave()`, lines 59-73) reads correctly on paper:
`ResourceLoader.Load` returning `null` is caught, warns, falls back to
`new SaveData()`. But this is unverified, not proven — `SaveTestRunner.cs`
only tests the *missing-file* case (deletes the save, checks defaults); it
never writes a malformed `.tres` and reloads through it. Whether
`ResourceLoader.Load` reliably returns `null` on a genuinely corrupt/
truncated file vs. throwing a C# exception (which would kill `_Ready()` on
this autoload and likely crash boot) is asserted by the code shape, not
measured by any test in this repo.

**Contract completeness: 3/10**

`SaveData` (`src/Core/SaveData.cs:9-12`) declares four fields:
`PlayerPosition`, `UnlockedAbilities`, `LastCheckpointId`, `WorldFlags`.
`SaveManager.cs` only ever writes two of them. Confirmed via repo-wide grep
for `PlayerPosition` and `WorldFlags` outside `SaveData.cs` — zero other
matches in `game/src`. Neither field is read or written anywhere:
- `PlayerPosition` is never set by `SaveManager`, and `EventBus.cs` has no
  signal carrying a position at all (grepped for `Position` — no matches).
  So a reload restores `LastCheckpointId`/`UnlockedAbilities` but the
  player's actual in-world location is silently `Vector3.Zero` on every
  load — the field exists in the contract but the plumbing to populate it
  doesn't exist anywhere in the codebase, not just in this file.
- `WorldFlags` is declared, defaulted to `new()`, and never touched again.
  Dead data.

Half of the documented save contract is inert. `CheckpointReached`/
`AbilityUnlocked` (the two events ARCHITECTURE.md's Save section names) are
covered; the two-thirds of `SaveData`'s payload not tied to those two
events is not.

**Specific issues found (ranked by severity):**
1. `PlayerPosition` is never written by `SaveManager`, and no `EventBus`
   signal exists to feed it — a loaded save cannot restore the player's
   actual location, only the checkpoint id/abilities. This defeats the
   basic expectation of a checkpoint-based save (resume near where you
   left off) even though `LastCheckpointId` alone might partially
   compensate if checkpoints double as spawn points elsewhere — that
   coupling isn't visible in this file and wasn't verified.
2. `WorldFlags` is fully dead: declared in `SaveData`, never read or
   written anywhere in `game/src`. Either an unfinished feature or a
   contract field that should be removed/documented as future work.
3. `LoadSave()`'s corrupt-file fallback (lines 61-70) is untested —
   `SaveTestRunner.cs` covers only the no-file case, not a malformed
   `.tres`. The graceful-degradation claim is a code-shape inference, not
   a verified behavior.
4. Same-frame checkpoint + ability unlock triggers two independent
   `ResourceSaver.Save` disk writes instead of one coalesced write — not
   a correctness bug (no race), just an avoidable double I/O.

**Recommendation:** Treat `PlayerPosition` as the priority gap — either
wire a real position-capture path (a new EventBus signal or a direct read
at checkpoint time) or, if checkpoints are meant to be the sole resume
mechanism, update ARCHITECTURE.md's `SaveData` contract to drop the field
so it stops looking like a silent bug. Then either implement `WorldFlags`
against a real use case or remove it. Add one `SaveTestRunner` case that
writes garbage bytes to `user://savegame.tres` and asserts `LoadSave()`
still returns a usable fresh save, to actually verify the corrupt-file
claim instead of inferring it from the code.

## Round 2 — 2026-09-04

**Contract completeness: fixed (was 3/10).** `SaveData.PlayerPosition` now has
a producer and a consumer: `SaveManager` captures the player's
`GlobalPosition` (via the `"player"` group — no compile-time dependency on
PlayerCamera) on `CheckpointReached`, persists it, and restores it on
`PlayerDied` through `RestorePlayerPosition()`. Uses only existing EventBus
events; no new vocabulary. This also closes World's round-1 finding that
checkpoint bookkeeping had no consumer.
**Evidence (headless round-trip, `scenes/RespawnTestScene.tscn`):** checkpoint
at (12.5, 3.25, 0) → player moved to (-99,-99,0) → `PlayerDied` → player back
at (12.5, 3.25, 0); same value re-read from disk. RESULT: PASS.
