## Round 1 — 2026-09-04

**AI behavior feel: 1/10**

`EnemyController.cs`'s state machine (Patrol/Chase/Attack/Stagger/Dead in
`_PhysicsProcess`, lines 103-143) reads correctly on paper: `TickPatrol`
walks between `_patrolTargetA`/`_patrolTargetB` via `NavigationAgent3D`,
flips at `PatrolPointArriveThreshold=0.3`, and `DetectionRadius=6.0`/
`LoseSightRadius=9.0` gate the Chase transition. None of that is visible in
the capture: the 13 sampled screenshots (frame_0015 through frame_0195,
spanning the full 200-frame / ~3.3s run) are **byte-identical** — same MD5
`1aa10e7a67c75ee845bc31a6b840dad1`, same 6811-byte size, verified via
`md5sum *.png`. Neither the enemy (BasicMelee, red capsule) nor
PlayerStandIn (blue capsule) moves a single pixel at any point.

This isn't a dead capture: `frametimes.csv`'s `wall_frame_ms` column shows
genuine, non-quantized per-frame jitter (4.230, 4.153, 4.277, 4.041ms, ...)
with a clean, expected ~26-30ms bump landing exactly every 15 frames
(screenshot cost, matches `--shot-every=15`) — the sim loop is demonstrably
running every physics frame. The freeze is specific to AI/movement output.

Root-cause hypothesis from reading `EnemyTestScene.tscn` +
`EnemyController.cs`: BasicMelee spawns at `(-4, 0.05, 0)`, exactly equal to
`PatrolPointA`. On the very first `TickPatrol` tick,
`DistanceXY(GlobalPosition, target) <= PatrolPointArriveThreshold` is
already true (distance ≈ 0), so it immediately flips to target B and calls
`SetNavTarget` — then `MoveToward` reads `NavAgent.GetNextPathPosition()`
the same frame, before `NavigationServer3D` has synced the new target
against the navmesh baked synchronously in
`EnemyTestSceneBootstrap._Ready()` moments earlier. A 1-2 frame desync from
that would be normal, but it doesn't explain 200 frames of total stillness
— something is making `GetNextPathPosition()` keep returning ≈current
position indefinitely. Not fully root-caused; needs per-frame logging of
`NavAgent.GetNextPathPosition()` / `IsNavigationFinished()` inside
`MoveToward` to pin down exactly where the path query goes dead.

Carried over from `playercamera.md`: `process_time_ms`/`physics_time_ms`
are still flat/quantized (8.160/6.239 for 171 rows, then a hard step to
29.668/0.207 for the rest) — same broken profiling hook, not fixed;
`wall_frame_ms` remains the only trustworthy timing column.

No combat, Chase, or Attack behavior occurred in this run, as expected —
`events.csv` is empty and there's no scripted attacker. But that's moot: an
enemy that never leaves its spawn point can't be evaluated on Chase/Attack
until Patrol proves it can move under its own power at all.

**Visual fidelity: 3/10**

Bare placeholder geometry as expected for an AI-logic testbed: red capsule
(enemy) and blue capsule (PlayerStandIn) on a flat grey floor slab, static
`Camera3D` (fixed, not this subsystem's job to move it). Blockout is clean
— no clipping/z-fighting, consistent lighting/shadow. Scored low not for
the placeholder art itself (expected, out of scope) but because the scene
never demonstrates anything happening — a single static screenshot would
show exactly as much as this entire "capture."

**Specific issues found (ranked by severity):**
1. Enemy patrol produces zero visible movement across the full capture
   (byte-identical PNGs, verified via MD5) — the one behavior this test
   scene exists to demonstrate is non-functional in practice, despite the
   state-machine code reading as correct.
2. Root cause not fully isolated — leading theory is a `NavigationAgent3D`
   path/target sync issue triggered by the enemy spawning exactly on top of
   `PatrolPointA`, but this needs debug instrumentation to confirm rather
   than more code reading.
3. `process_time_ms`/`physics_time_ms` remain the same broken
   quantized/flat data flagged in `playercamera.md` rounds 1-2 — still
   unfixed, still dead weight in the CSV beyond `wall_frame_ms`.

**Recommendation:** Do not proceed to scoring Chase/Attack/Stagger until
Patrol is confirmed moving on screen. Add per-frame debug output around
`EnemyController.MoveToward`/`NavAgent.GetNextPathPosition()`, re-run this
exact capture command, and confirm the enemy's screen-x actually changes
frame to frame before re-scoring. This is a hard blocker, not a polish
item — an AI subsystem whose enemy never leaves its spawn tile has not yet
demonstrated its core contract.

## Round 2 — 2026-09-04

**AI behavior feel: fixed (was 1/10 — enemy never moved).**
Root cause was NOT the spawn-on-PatrolPointA hypothesis. Godot's 3D navmesh
pathing is built for horizontal (XZ) surfaces; this is a 2.5D side-scroller on
the X/Y plane, so patrol targets never mapped onto the baked navmesh and
`GetNextPathPosition()` returned the agent's OWN position every tick (traced:
pf=40/80/120/160 all `next==pos`, dx=0). `NavigationAgent3D` only recomputes
when `TargetPosition` changes, and it never changed because the enemy never
arrived — permanent freeze. Replaced with direct X-axis steering toward an
explicit `_steerTarget`.
**Evidence:** 8/8 sampled frames now have distinct MD5s (previously 8/8
identical); enemy capsule moves x≈490px → x≈613px between frame 25 and 150.

## Round 2 (independent verification) — 2026-09-04

**AI behavior feel: 5/10** (was 1/10)
**Visual fidelity: 5/10** (was 3/10)

### The fix is real. Verified, not taken on trust.

The round-2 claim holds up under independent measurement. All 8 PNGs have
distinct MD5s (round 1: 8/8 identical). I decoded each frame with a
zlib/PNG reader and took the centroid of red-dominant pixels
(`r>110 && r>1.9g && r>1.9b`):

| frame | red centroid px | world X | delta px |
|-------|-----------------|---------|----------|
| 0025 | 490.24 | -3.047 | — |
| 0050 | 520.50 | -1.972 | +30.26 |
| 0075 | 543.23 | -1.164 | +22.73 |
| 0100 | 566.72 | -0.330 | +23.49 |
| 0125 | 590.56 | +0.517 | +23.84 |
| 0150 | 614.09 | +1.353 | +23.53 |
| 0175 | 637.97 | +2.201 | +23.88 |
| 0200 | 661.45 | +3.035 | +23.48 |

Monotonic, steady, non-erratic. The world-X column is derived, not guessed:
`Camera3D` sits at `(0,3,15)` with identity rotation and default 75 degree
vertical FOV, so `screen_x = 576 + (648/2)/tan(37.5) * X/15 = 576 + 28.15*X`.
That model predicts the blue `PlayerStandIn` at its authored `X=12` to land at
913.8 px; measured 913.65 px in all 8 frames (0.15 px error), which both
validates the projection and confirms the player never moved.

The decisive number: the enemy spawns at `X=-4` and is at `X=+3.035` by
frame 200, i.e. 7.035 units in 3471.6 ms of summed `wall_frame_ms` =
**2.026 u/s against a commanded `PatrolSpeed = 2.0`** — within 1.3%. The
steering isn't merely non-zero, it runs at exactly the authored speed. The
first sampled interval (+30.26 px vs a steady ~23.6) is a capture artifact,
not an AI defect: `wall_frame_ms` row 1 is 61.232 ms, so Godot ran several
catch-up physics ticks inside that one rendered frame, inflating displacement
per *rendered* frame early on. It settles to 1:1 immediately after.

The diagnosis in the round-2 entry is also correct on the merits, not just
lucky. Godot bakes `NavigationMesh` from walkable surfaces selected by
up-vector and max-slope on the XZ plane; a game whose movement plane is X/Y
with Z locked genuinely cannot map its patrol targets onto that mesh, and
`NavigationAgent3D` genuinely only re-paths when `TargetPosition` changes —
so a degenerate first query really does latch permanently. Dropping the
navmesh here was the right call, and round 1's spawn-on-PatrolPointA
hypothesis is correctly retired.

### Why this is a 5 and not a 7.

**One straight line is all that was proven.** `PatrolPointB` is at `X=4` and
`PatrolPointArriveThreshold` is `0.3`, so the direction flip fires at
`X≈3.7`. The capture ends at `X=3.035` — roughly 0.67 units, about 20 frames,
short of the turnaround. The single most important behavior after "it moves
at all" is still unverified by pixels, and this log's own round 1 established
that code which "reads correctly on paper" is worth nothing here.

**Chase and Attack are geometrically unreachable in this scene.** The enemy's
patrol span is `[-4, +4]`; `PlayerStandIn` is authored at `X=12`;
`DetectionRadius` is `6.0`. Minimum possible gap is 8 units. Worse,
`PlayerStandIn._PhysicsProcess` drives itself solely from
`Input.GetAxis("ui_left","ui_right")`, and an automated capture supplies no
input — so it is parked at 12 by construction. The empty `events.csv` is
therefore guaranteed by the scene layout, not evidence about the AI. Three of
five states (Chase, Attack, Stagger) remain completely unexercised after two
rounds.

**The round-1 freeze class is relocated, not eliminated.** `TickPatrol` calls
`SetNavTarget` *only* inside the `withinThreshold` flip branch
(EnemyController.cs:195-200), while `TickChase` overwrites `_steerTarget`
with the player's position every single tick (line 213), and `TransitionTo`
resets nothing. So on any Chase→Patrol or Stagger→Patrol return, the enemy
keeps steering toward the stale last-known-player X. If it settles within the
0.05 deadband of that stale point without ever coming within 0.3 of an actual
patrol marker, `velocity.X` is pinned to 0 forever — a permanent freeze with
the same on-screen signature as round 1, reached by a different path. This is
a live bug in shipped code, and the current capture cannot surface it because
Chase never runs.

**The trade-off, judged on its merits.** Direct X steering is the correct
*tool* for a 2.5D platformer — a navmesh cannot express ledges, drops, or
jumps anyway, so the entry's reasoning is sound. But what landed is the
minimum viable version of that tool, and two gaps are real, not pedantic:

- *No ledge awareness.* The enemy will walk straight off any platform edge.
  Lost Crown's grunts hold their ledges; this one cannot. In a metroidvania
  with pits this is a gameplay bug, not a polish item.
- *No wall handling.* `MoveToward` pins `velocity.X` to `±speed` regardless of
  contact, and the flip fires only on proximity to the patrol point. An enemy
  blocked by geometry short of its target presses into that wall indefinitely
  and never turns around — a third route to the same freeze.

Both are fixed by two short raycasts (an ahead-and-down ledge probe, an ahead
wall probe) feeding a shared "blocked -> flip" path. That is a small addition,
which is exactly why its absence is worth flagging rather than excusing.

**Motion quality.** `velocity.X = Mathf.Sign(dx) * speed` is bang-bang:
instant 0 to 2.0 u/s, instant stop, no accel or decel ramp. Correct, and it
reads as robotic. Lost Crown's enemies carry visible weight and anticipation
into a turn. Feel-level, not a blocker, but it is the difference between
"moves" and "feels alive."

### Visual fidelity: 5/10

Scored strictly as an AI testbed; placeholder art is out of scope and not
penalized. Round 1's 3 was for a scene that demonstrated nothing — that is
fixed, and legibility is fine: clean silhouettes, consistent directional
lighting and shadow, no clipping or z-fighting. It stops at 5 because the
framing actively works against the subsystem it exists to show. The enemy
occupies ~21 px of a 1152 px frame, the action sits in a narrow horizontal
band, and the upper half is empty grey. There is no debug overlay — no state
label, no target marker, no detection-radius gizmo, no visible patrol
markers — which is why verifying this fix required reverse-engineering world
coordinates from pixel centroids and the camera transform. For an AI testbed
that overlay is the highest-value visual work available and costs very little.

### Issues, ranked

1. **`_steerTarget` is never re-established on entry to Patrol**
   (EnemyController.cs:193-202 vs 213). Stale player position leaks across the
   Chase→Patrol transition and can pin `velocity.X` to 0 permanently. Same
   observable failure as round 1. Fix: set the steer target on Patrol entry,
   not only on flip.
2. **Chase/Attack/Stagger structurally untestable in `EnemyTestScene.tscn`.**
   Player at `X=12` vs patrol max `X=4` and `DetectionRadius=6` gives an
   8-unit minimum gap; `PlayerStandIn` moves only on keyboard input a headless
   capture never sends. The empty `events.csv` proves nothing.
3. **No ledge probe.** Enemy walks off platform edges. Real gameplay defect in
   a pit-bearing metroidvania.
4. **No wall/blocked handling.** A blocked enemy presses into geometry forever
   and never flips.
5. **Capture ends ~20 frames before the patrol turnaround** (`X=3.035` vs flip
   at `X≈3.7`). The flip remains unproven by pixels.
6. **`NavAgent` is vestigial but still driven.** `SetNavTarget` assigns
   `NavAgent.TargetPosition` every Chase tick (line 334), forcing a
   `NavigationAgent3D` re-path per frame against a navmesh it cannot use, for
   a result nothing reads. Wasted work, and misleading to future readers.
7. **`_navRetryTimer` declared and never used** (line 69). Dead field.
8. **`process_time_ms` / `physics_time_ms` still broken**, third round running.
   `wall_frame_ms` remains the only trustworthy timing column.

### Recommendation — stop condition NOT met

Threshold is both axes >= 7; actual is 5 and 5. Nor is this a plateau — the
behavior axis moved 1 to 5 in one round, which argues for continuing, not
stopping. Round 3, in order:

1. Fix issue 1 (`_steerTarget` on Patrol entry). It is a few lines and it
   closes the last known route back to the round-1 failure.
2. Re-author the test scene so Chase is reachable: move `PlayerStandIn` to
   roughly `X=6`, or script it to walk into detection range on a timer rather
   than waiting for keystrokes that never come.
3. Extend the capture to >= 400 frames so at least one full patrol turnaround
   and one Chase→Attack→Patrol cycle land on film.
4. Add the ledge and wall raycasts with a shared blocked-to-flip path.
5. Add a debug overlay (state name, steer target, detection radius) so the
   next round is verifiable by looking rather than by reconstructing the
   camera projection.

Items 1-3 are the blockers. Until a capture shows the enemy turn around and
enter at least one non-Patrol state, this subsystem has demonstrated
locomotion but not yet AI.
