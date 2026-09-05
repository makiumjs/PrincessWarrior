## Round 1 — 2026-09-04

**Responsiveness/clarity: 2/10**

`events.csv` from the capture (`docs/critic-captures/ui/events.csv`) contains
only its header row — zero of `UiTestDriver`'s 8 steps (`PlayerDamaged`,
`AbilityUnlocked`×3, `CheckpointReached`×2) were logged across the full
400-frame run. Two independent, stacked causes, both verified rather than
assumed:

1. **Timer never reaches its first Timeout.** `frametimes.csv`'s
   `wall_frame_ms` column sums to **1848.92ms** across all 400 rows (checked
   via `awk` sum, not eyeballed) — the engine ran unthrottled at ~4.1ms/frame
   (~240fps), not the assumed 60fps/16.7ms. `UiTestDriver.IntervalSeconds`
   is 2.0s, driven by real per-frame delta, so the whole capture only covers
   ~1.85 simulated seconds — under the 2.0s needed for step 0 to fire at
   all. The task brief's "~120 frames at 60fps ≈ one event" assumption
   doesn't hold under this runner.
2. **`CaptureRunner` doesn't listen for 2 of the 3 event types anyway.**
   `src/Core/Testing/CaptureRunner.cs` lines 87-100 (`SubscribeEventBus`)
   wires `Jumped/DoubleJumped/Dashed/WallJumped/Landed/PlayerDamaged/
   PlayerDied/EnemyDamaged/EnemyDied` — `AbilityUnlocked` and
   `CheckpointReached` are absent. Even with issue 1 fixed, only
   `PlayerDamaged` (driver steps 0 and 4) could ever appear in
   `events.csv`; ability-unlock and checkpoint events are structurally
   invisible to this harness.

Confirms in the images: `frame_0040.png`, `frame_0200.png`, `frame_0400.png`
(spanning the full capture) are **pixel-identical** — static "100 / 100"
health bar top-left, no ability icons, no checkpoint prompt, at frame 40 and
still at frame 400. That's consistent with zero events firing, not
inconsistent with it, so I can't separately confirm or rule out a `Hud.cs`
update bug — the harness failure blocks verification entirely, which is
itself disqualifying for a subsystem whose contract is "verified end-to-end,
not asserted from reading the code."

`frametimes.csv` spikes to ~27-34ms recur at frames 41/81/121/161/201/241/
281/321/361 — exactly every 40 frames, matching `--shot-every=40` — so
that's accounted-for PNG-encode cost, not an unexplained hitch.
`process_time_ms`/`physics_time_ms` remain the same flat/quantized broken
columns already flagged in `playercamera.md` rounds 1-2 (unfixed, not
re-litigating here).

**Visual fidelity: 4/10**

Same scope disclaimer as playercamera: placeholder `ColorRect`/`Label`
icons are expected, real art is a different pass. What's actually visible —
the health bar/label, top-left, `MarginContainer` at (24,24)-(264,64) — is
clean and legible, no overlap or clipping. Reading `scenes/ui/Hud.tscn`
(since nothing else ever rendered): the ability row (40×40 `ColorRect`
icons, distinct colors per ability, `HBoxContainer` below the health bar)
and the top-center checkpoint label are sanely laid out on paper — no
obvious overlap with the health panel. Capped at 4 because 3 of 4 HUD
elements were never actually observed on screen in this capture, only
inferred from the scene file, which isn't the same as a verified render.

**Specific issues found (ranked by severity):**
1. `CaptureRunner.SubscribeEventBus()` (`src/Core/Testing/CaptureRunner.cs`
   lines 87-100) never subscribes to `AbilityUnlocked` or
   `CheckpointReached` — structurally blind to 2 of the 3 event types this
   HUD exists to react to, independent of any timing issue.
2. `UiTestDriver`'s 2.0s step interval, measured against this capture's
   actual ~4.1ms/frame unthrottled rate, needs ~488 frames just to reach the
   first step — the requested 400-frame/`--frames` capture never got there
   (confirmed via summed `wall_frame_ms` = 1848.92ms < 2000ms). Either the
   driver needs a much shorter interval for capture purposes, or the capture
   command needs enough frames budgeted against measured, not assumed, fps.
3. Because of 1+2, this round produced **no evidence either way** about
   whether `Hud.cs`'s event handlers (`OnPlayerDamaged`, `OnAbilityUnlocked`,
   `OnCheckpointReached`) actually update the UI correctly — that code reads
   fine on inspection (clamped health, flag-checked ability reveals,
   timer-driven checkpoint hide) but is unverified.

**Recommendation:** Fix the capture harness before re-scoring this
subsystem at all: (a) add `AbilityUnlocked`/`CheckpointReached` to
`CaptureRunner.SubscribeEventBus`, (b) either drop `UiTestDriver.
IntervalSeconds` to ~0.5s for capture purposes or raise `--frames` well
past 488 at measured fps, then re-run once and confirm `events.csv` actually
contains rows before trusting any HUD responsiveness claim.

## Round 2 — 2026-09-04

**Round 1's 2/10 is VOID — it measured the harness, not the HUD.** Both causes
found in round 1 were capture-tool bugs, now fixed: (a) the harness ran
uncapped (~240fps), so a 400-frame capture spanned only ~1.7s real and the
driver's 2s Timer never fired — now capped to 60fps; (b) `CaptureRunner` never
subscribed to `AbilityUnlocked`/`CheckpointReached` — now subscribed, and both
CSVs are `AutoFlush` so a target scene quitting early can't empty them.
**Evidence:** `events.csv` now logs PlayerDamaged (frame 113), AbilityUnlocked
(233), CheckpointReached:shrine_01 (353) — the designed ~2s cadence. Frames
confirm the HUD reacts correctly: health 100/100 → 85/100, "Double Jump" icon
revealed, "Checkpoint Reached: shrine_01" prompt visible. `Hud.cs` was correct
all along.
