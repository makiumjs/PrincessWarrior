## Round 1 — 2026-09-04

**Movement feel: 4/10**

Tunables in `PlayerController.cs` are individually sane: `JumpHeight=2.5m`/
`JumpApexTime=0.35s` derives `_gravity`/`_jumpVelocity`≈14.28 m/s via
`h=0.5*g*t²`, a normal metroidvania apex; `CoyoteTimeWindow=0.1s` and
`InputBufferWindow=0.15s` are standard values; `WallJumpImpulse=13` sits
close to the derived jump velocity (14.28), so wall-jump doesn't feel like
an outlier; `GroundAcceleration=60`/`GroundFriction=70` against
`MoveSpeed=8` gives a ~0.13s ramp — snappy, not floaty.

Input latency from `events.csv` (frame × 16.7ms): `jump` pressed frame 20 →
`Jumped` fired frame 20 (0ms). `dash` pressed frame 70 → `Dashed` fired
frame 70 (0ms). But the second `jump` tap at frame 35 → `DoubleJumped` only
fires at frame 38, a 3-frame / ~50ms gap. Reading the double-jump branch in
`_PhysicsProcess` (lines 242-250), it resolves in the same `if/else` chain
as the ground jump, off the same `_jumpBufferTimer`, with no extra timer or
threshold gating specifically double jump — nothing in the code explains a
buffering delay unique to this path, so the 3-frame gap reads as an
unexplained inconsistency (bug or capture artifact), not an intentional
design, and is worth root-causing before trusting the double-jump feel.

The 5 PNGs (frames 20/40/70/80/150) undercut the clean event timing: the
background (platform, wall) sits at the **identical pixel position in every
single frame** while the capsule drifts only slightly and is **entirely
absent from frame 150** — with `move_right` held frames 5-145, the camera
never followed the player at all, contradicting ARCHITECTURE.md's
requirement that this subsystem owns "side-scroll follow" camera framing.
Separately, the capsule moves only ~10px between frame 70 and frame 80 (the
dash window, `DashDuration=0.18s`≈10.8 frames) — far short of what a
`DashDistance=4.5m` burst should look like on screen next to the ~150px it
covered walking over the preceding 50 frames.

`frametimes.csv` is also suspect as evidence, independent of the above:
`process_time_ms`/`physics_time_ms` are **exactly** `10.785`/`7.254` for
110 consecutive rows, then jump to **exactly** `33.454`/`13.077` for the
remaining 39 rows with zero jitter anywhere in either segment. Real
`Performance.get_monitor(TimeProcess)` output always carries frame-to-frame
noise; this pattern (perfectly quantized, one hard step) reads as a stale/
broken profiling hook or a synthetically-written CSV rather than a genuine
capture, which is a problem given ARCHITECTURE.md explicitly requires this
subsystem be "verified end-to-end via headless/windowed run, not asserted
from reading the code" — right now the capture doesn't clear that bar.

**Visual fidelity: 3/10**

Explicitly not a meaningful comparison to Lost Crown yet — the test scene
is a bare capsule placeholder on grey blockout geometry (flat platform,
one wall, one floating ledge), and real character art is ProceduralArt's
scope, not wired in here. Scoring only what's shown: the blockout is clean
and legible (flat grey shading, no z-fighting/clipping visible, consistent
lighting across frames), which is a reasonable baseline for a physics test
scene. It loses points because the camera framing itself — which IS this
subsystem's job, not ProceduralArt's — never adjusts, so even as a
blockout the shot composition is static and eventually frames an empty
room once the character leaves the fixed view.

**Specific issues found (ranked by severity):**
1. Camera does not track the player (verified via pixel-identical
   backgrounds across frame_0020/0040/0070/0080/0150.png) — player exits
   the frame entirely by frame 150 despite `move_right` being held the
   whole capture. This is a required, documented behavior of this
   subsystem ("side-scroll follow, look-ahead, dead-zone"), not optional
   polish.
2. `frametimes.csv` shows zero-jitter, two-value-only data (110 rows flat
   at one value, 39 rows flat at another) — not credible as a genuine
   per-frame `Performance.get_monitor` capture. Either the profiling hook
   is broken or the capture tooling isn't actually sampling live frames;
   needs to be fixed before later rounds' frame-time claims can be trusted.
3. Double-jump event latency (3 frames / ~50ms) is inconsistent with
   jump/dash (0 frames each) and unexplained by the code's buffering logic
   — likely a real timing bug in the double-jump branch, not intentional.
4. Dash's on-screen displacement (frame 70→80) looks far smaller than
   `DashDistance=4.5m` over `DashDuration=0.18s` should produce given the
   walk-speed pixel scale established by the earlier frames — worth
   confirming the dash is actually applying full velocity for its full
   duration.

**Recommendation:** Fix the camera-follow (issue 1) first — it's a core
contract requirement and currently appears entirely non-functional in the
capture. Then fix or replace the frame-time capture mechanism (issue 2) so
future rounds' performance data is trustworthy, and re-run the capture to
confirm/deny issues 3 and 4 before re-scoring.

## Round 2 — 2026-09-04

**Movement feel: 7/10**

The camera-follow fix is real, verified against the 6 PNGs, not just the
claimed numbers. The wall's right edge (the bright vertical strip) moves
monotonically leftward across every sampled frame: ~890px (frame_0010) →
873 (0030) → 838 (0060) → 765 (0090) → 630 (0120) → 578 (0150) — matching
the claimed ~880→~575 shift, and critically, direction never reverses, so
there's no visible oscillation/jitter frame-to-frame. The player capsule
stays on-screen in every frame, including frame_0150: at first glance it
looks absent again (round 1's bug), but a zoomed crop confirms it's present
at ~x553, just low-contrast because it sits in the wall's shadow. Capsule
screen-x across the 6 frames (548, 548, 570, 617, 583, 553) stays inside a
~70px band roughly centered on the 1152px-wide frame — a working dead
zone, not a rigid lock.

Two real issues keep this off 8+:

1. Background scroll speed is far from uniform even though the player's
   held input (`move_right`) is constant: Δpx between samples is -17, -35,
   -73, -135, -52 over equal 30-frame gaps (10→150), i.e. roughly
   1×/1.4×/2.9×/5.3×/2× the baseline rate. That's a lerp/spring camera
   lagging behind the dash (frame 70) and then visibly catching up hardest
   around frames 90-120 — a rubber-band effect. Plausible as intentional
   look-ahead smoothing, but at this magnitude it would read as sluggish-
   then-lurching in motion, not smooth tracking.
2. `wall_frame_ms` in frametimes.csv has a genuine, unexplained periodic
   hitch: frames 11, 21, 31, 41, 51, 61, 71, 81, 91, 101, 111, 121, 131, 141
   all spike to 26-33ms — a strict every-10-frames cadence, 14 occurrences,
   ~2x the 16.7ms/60fps budget, uncorrelated with any input event in
   events.csv. Frame 1's 50ms spike is the expected/acceptable startup
   cost; this recurring one is not — it would be a visible periodic
   stutter in real play and needs root-causing (smells like a
   fixed-interval Timer, tween, or GC/allocation tick).

Double-jump latency (round 1's issue 3) can't be fully re-verified: this
round's events.csv only logs one frame per event (`35,DoubleJumped`) with
no separate press-vs-fire columns like round 1 had. Taken at face value —
press and fire both landing on frame 35 — the 3-frame delay looks resolved,
but the capture format regressed in a way that makes this a inference, not
a measurement.

`process_time_ms`/`physics_time_ms` are still the same broken two-value-
quantized data as round 1 (flat 10.726/7.334 for 148 rows, then a hard
step to 30.173/15.431 for the last 2) — not a new problem, and per this
round's brief `wall_frame_ms` (genuinely noisy, non-repeating decimals) is
the trustworthy column, but the other two remain unfit for future rounds'
claims and should eventually be fixed or dropped from the CSV.

**Visual fidelity: 5/10**

Same disclaimer as round 1: bare capsule on grey blockout, ProceduralArt
not integrated, not a real comparison to Lost Crown. Scored up from 3
because the thing that was actually broken last round — camera framing,
which the metroidvania genre relies on for readability — now does its job:
the shot follows the action instead of statically framing an empty room.
Blockout shading/geometry is unchanged (clean, no clipping/z-fighting).
Capped below 7 because there's still no character, environment art, or
lighting variation to judge — that gap is out of this subsystem's scope,
not a camera defect.

**Specific issues found (ranked by severity):**
1. Camera-follow rate is non-uniform relative to constant held input —
   background scroll accelerates ~5x then decelerates back down around the
   frame-70 dash (see Δpx figures above). Likely a lerp/spring smoothing
   constant that's too loose; tightening it (or adding a velocity-matched
   term during dash) would remove the rubber-band feel.
2. `wall_frame_ms` spikes to 26-33ms on a strict every-10-frame cadence
   (14 occurrences), unrelated to any gameplay event — a real periodic
   hitch worth root-causing before it's mistaken for noise.
3. `process_time_ms`/`physics_time_ms` remain quantized/flat exactly as in
   round 1 (still broken profiling hook) — harmless this round only because
   `wall_frame_ms` was added as the trustworthy signal; should still be
   fixed so those columns aren't dead weight in the CSV.
4. events.csv lost the press-vs-fire granularity round 1 had, so the
   double-jump latency bug flagged in round 1 can't be independently
   re-measured this round, only inferred from a single timestamp.

**Comparison to Round 1:** The core fix worked. Round 1's headline failure —
camera not tracking at all, player vanishing off-frame by frame 150 — is
gone: background geometry now shifts continuously and the player stays
framed (verified via zoomed inspection, not just the claim). What's left is
smoothing tuning (rubber-band around the dash) and a new perf question (the
periodic 10-frame hitch) rather than the fundamental non-functional camera
from round 1.

**Recommendation:** Movement feel clears the 7/10 threshold — stop
re-scoring camera-follow-exists as a question, it's answered. Two follow-ups
before calling this subsystem fully done: (a) tune the follow smoothing so
scroll speed doesn't swing ~5x during a dash, (b) root-cause the every-10-
frame `wall_frame_ms` spike. Visual fidelity is capped by scope (no art
integrated yet), not a defect in this subsystem — don't loop camera work
again to chase that score; it moves only when ProceduralArt's output is
wired into this scene.
