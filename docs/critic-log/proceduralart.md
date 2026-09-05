## Round 1 — 2026-09-04

**Technical execution: 3/10**

Capture command requested 90 frames / shots every 10 (9 PNGs expected) but
only 4 landed (`frame_0010/0020/0030/0040.png`, under
`game/docs/critic-captures/proceduralart/` — the `--out` path resolves
relative to the Godot project root, not the repo root). Console log
confirms why: `ProceduralArtTestScene.tscn` still carries the leftover
`CaptureTestShot.cs` (`FramesToWait=40`, hard `GetTree().Quit()` at frame
45), which fired mid-capture and printed `[CaptureTestShot] Saved capture
to C:/Users/Mako/Desktop/Gioco/docs/proceduralart-test-capture.png` — it
also silently overwrote the old static reference screenshot as a side
effect. Minor issue, not scored against this round, but worth removing now
that `CaptureRunner` supersedes it.

The scene sets `ProceduralCharacter.RunBlend = 1.0` (pure run clip, no
idle blend), and `BuildRunAnimation` in `ProceduralAnimationBuilder.cs` is
a 0.55s loop with large amplitude (`swing=0.9` rad ≈ 51° thigh swing,
opposite-arm-swing, hip bounce). Frames 10→40 span 30 engine frames — at
60fps that's 0.5s, i.e. **~90% of one full run cycle** — so a working
run animation should show the legs scissor from fully-forward to
fully-back and the arms swap sides in this window alone. Instead all 4
frames are visually indistinguishable: same leg position (both together,
slightly forward), same arm angle, same torso lean, checked frame-by-frame
side by side. `sha256sum` on the 4 PNGs confirms they are *not* byte-
identical, so something is changing pixel-for-pixel (likely AA/shadow
jitter or a sub-pixel pose delta), but nothing at a scale a player would
ever perceive as "running." Given the AnimationTree wiring itself reads
correctly (`AnimationNodeBlendTree` with idle/run `AnimationNodeAnimation`
nodes into a `Blend2`, `Tree.Active=true`, `blend_amount` re-set every
`_Process`), this looks like an animation-time-advancement bug, not a
graph-wiring bug — worth instrumenting `Tree.Get("parameters/blend/…")`
or the AnimationPlayer's `current_animation_position` frame-by-frame to
find where the clock is stalling. This is the headline failure: the one
thing this axis is supposed to measure (does pose visibly change over
time) reads as no.

Shader noise is real where it's checkable: the character's skin material
shows clear blotchy/mottled variation (not flat color), and the wood beam
shows a banded grain-like pattern — both consistent with the hash-noise
described in `ProceduralMeshFactory.cs`. The stone platform's visible
surface (the grey ground plane filling most of the frame) shows no
perceptible noise/mottling at this camera distance — can't confirm it's
doing anything beyond flat shading from these shots alone. Geometry is
otherwise clean (no inverted-normal black patches, no z-fighting between
overlapping meshes), but limb capsules meet the torso with a hard,
unblended intersection at both shoulders and hips — visible clipping, not
a seamless joint — and the character's cast shadow on the ground is a
jagged, non-humanoid silhouette in every frame, unrelated in shape to the
capsule rig above it, which reads as a shadow-mapping artifact on the thin
cylindrical geometry.

**Visual fidelity: 2/10**

Scored for real this time, against Lost Crown's hand-painted, richly
detailed character and environment art — and it's not close, as expected.
What's on screen is an unclothed pink/red mottled capsule mannequin: no
face, no hands, no silhouette read (arms and legs are uniform tubes), no
material distinction between "skin" and "clothing," and the head is
cropped out of frame in all 4 captured shots so even the mottled-texture
read can't be judged above the neck. The wood beam at least registers as
"a wooden beam" from its color and banding; the stone platform doesn't
register as stone at all from this shot — it reads as a flat grey floor.
Nothing here would survive a side-by-side with even Lost Crown's simplest
background props, let alone its player character — which the project's own
docs already flag as an accepted ceiling for procedural generation, not a
surprise.

**Specific issues found (ranked by severity):**
1. Run animation (`RunBlend=1.0`) does not produce visible pose change
   across ~90% of one loop cycle (frames 10→40, PNG hashes differ but
   frames are visually identical) — the core "does it animate" question
   this round was meant to answer comes back no, despite the AnimationTree
   graph itself being wired correctly. Needs frame-by-frame instrumentation
   of the animation clock, not just a re-read of the graph code.
2. Leftover `CaptureTestShot.cs` on `ProceduralArtTestScene.tscn` quits the
   process at frame ~45, truncating this round's 90-frame/9-shot request
   to 4 shots, and overwrote the old static reference PNG as a side effect.
   Should be removed now that generic `CaptureRunner` covers this scene too
   (not removed here per instructions — flagging only).
3. Character head is cropped out of frame in every captured shot — camera
   framing in the test scene needs to pull back or tilt to actually show
   the rig being evaluated.
4. Hard, unblended capsule intersections at shoulder/hip joints, and a
   ground shadow shape that doesn't match the character silhouette —
   both read as unfinished geometry/shadow-mapping issues on the rig.

**Recommendation:** Fix issue 1 first — a procedural-animation subsystem
that doesn't visibly animate its flagship run cycle is a functional defect,
not a style/fidelity gap, and blocks any further scoring of "movement"
here. Then remove the leftover `CaptureTestShot.cs` and widen the test
camera to include the head before the next round. Visual fidelity (2/10)
should not be re-chased — it's bounded by the procedural-primitive
approach itself, exactly as the project's own docs acknowledge, and no
amount of tuning noise parameters closes that gap.

## Round 2 — 2026-09-04

**Technical execution: the round-1 "animation stall" was a FALSE POSITIVE.**
The animation was running correctly all along — traced `Hips/UpperLegL`
rotation over frames: 0 → 0.52 → 0.72 → 0.31 rad (up to ~41°). The stall was
an artifact of the capture harness, which ran uncapped at ~240fps on this GPU,
so the 4 sampled shots were only ~40ms apart in real time on a 0.55s loop —
near-identical poses by construction. Harness now caps to `Engine.MaxFps = 60`.
The leftover `CaptureTestShot.cs` (which quit at frame ~45 and overwrote the
reference PNG) has been removed from the scene.
**Evidence:** 11/11 requested frames captured (was 4), and poses visibly differ
between frame_0008 and frame_0024 — arms mirrored, legs in opposite phase.
Visual fidelity vs Lost Crown stands at round 1's 2/10: procedural capsule
mannequin, acknowledged ceiling.

## Round 2 (independent verification) — 2026-09-04

**Technical execution: 6/10** (was 3/10)
**Visual fidelity vs Lost Crown: 2/10** (unchanged)

### The "false positive" claim: UPHELD on outcome, incomplete on cause

The animation demonstrably runs. Comparing the 5 frames under
`game/docs/critic-captures/proceduralart-round2/`:

- **`frame_0008` vs `frame_0024`** — a full antiphase swap, unmistakable.
  In 0008 the screen-left arm is long and extended down past the hip
  (elbow visible, forearm reaching to ~y=185) while the screen-right arm is
  a short foreshortened stub (~y=130); the screen-left leg is straight and
  planted at the ground (~y=330) with the screen-right leg bent and lifted
  (~y=250). In 0024 **every one of those four limbs is on the opposite
  side**: screen-left arm is the stub, screen-right arm is the long
  extended one, screen-right leg is straight and planted, screen-left leg
  is bent and lifted. That is a half run cycle, exactly as
  `BuildRunAnimation` specifies (`legR = sin(phase + pi)`).
- **`frame_0040` = `frame_0008`, `frame_0056` = `frame_0024`,
  `frame_0072` = `frame_0008`** — and this is confirming evidence, not a
  problem. The capture stride is 16 frames at the newly capped 60fps =
  0.2667s; the clip `Length` is 0.55s. 0.2667 / 0.55 = **0.485 of a cycle
  per sample**. The predicted result is exactly what is on disk: samples
  alternating between two near-antiphase poses with a slow ~1.5%-of-cycle
  backward drift per step (visible as the screen-right lower leg sitting
  slightly lower in 0072 than in 0008). **The sampled frames match the
  clip's declared period to within the predicted drift.** The animation
  clock is advancing at the correct rate. Round 1's headline finding is
  refuted.

The claimed *mechanism* is only partly verified. I did not run the harness,
and the arithmetic does not fully close: round 1's shots at ~240fps spanned
frames 10 to 40 = 0.125s = **23% of a cycle**, over which the formula swings
the thigh ~0.4 rad (23 degrees). That is not nothing. The reason 23 degrees
of thigh swing was invisible is a second, *unacknowledged* cause: **the test
camera is nearly head-on to the direction of motion.** Run cycles swing in
the sagittal plane; a frontal camera foreshortens the entire motion away.
Even in the good frames above, the pose change is legible mainly through
*which arm looks long vs. stubby* — i.e. through foreshortening, not through
readable limb travel. The fps cap fixed the sampling; it did not fix the
viewing axis that made the aliasing so convincing in the first place.

### Round-1 issues not claimed as fixed — all still present

- **Hard, unblended capsule intersections at shoulder and hip: still
  visible, and worse at some phases.** In `frame_0024` and `frame_0056` the
  screen-left upper arm reads as a *detached floating stub* beside the
  torso rather than a limb attached at a shoulder — no deltoid mass, no
  blend, just a capsule end butting a capsule side. The hips show the same
  hard boundary where the thigh capsules enter the pelvis.
- **Shadow silhouette: still broken, and now provably not the rig's.** The
  ground is littered with jagged polygonal dark shards (the cluster at
  roughly x=300-560 / y=280-410 and the patch at x=620-800 / y=310-390).
  These are **static across `frame_0008` to `frame_0024`**, a full
  half-cycle over which the body pose completely swaps. A cast shadow of an
  animated rig cannot be pose-invariant. These are shadow-map artifacts
  (acne / peter-panning on the thin cylindrical geometry and the ground
  plane), not the character's shadow. **No humanoid shadow silhouette is
  identifiable in any of the 5 frames.** The one shadow that behaves
  correctly is the wood beam's long diagonal.
- **Head still cropped out of frame in all 5 shots** (round-1 issue 3, not
  claimed fixed and not fixed). At best the neck / base of the skull peeks
  in at the top edge in `frame_0024` and `frame_0056`.

### Procedural shaders: two of three real

- **Skin: real noise, confirmed.** Clear irregular mottled blotching across
  torso and limbs, non-repeating, at multiple scales. Not flat color.
- **Wood: real variation, but reads as banding, not grain.** The beam shows
  distinct light/dark transverse bands plus a repeating rectangular pattern
  on the end face. It registers as "wooden beam," but the regularity gives
  away a stripe function rather than organic grain.
- **Stone/ground: still no perceptible variation.** The grey plane filling
  the lower two-thirds of every frame is flat. Unchanged from round 1.

### Visual fidelity: 2/10 stands, and precisely why

Motion working does not move this axis — this axis is the *look*. Against
Lost Crown's hand-painted character art, the gaps, ordered by how badly they
read:

1. **No silhouette.** This is the big one and it is the whole ballgame for a
   fast 2.5D platformer, where the player reads the character by outline at
   speed. Limbs are uniform-diameter tubes: no taper, no shoulder mass, no
   neck, no hair, no cloth. Lost Crown's Sargon is legible as a
   black-on-white shape from his scarf, sash, hair and blade alone. This rig
   is legible as "sausages."
2. **No face.** Not stylized-minimal — absent. And moot anyway while the
   head is cropped out of frame.
3. **No hands.** Arms terminate in blunt capsule caps. Nothing to read a
   gesture, a grip, or a weapon from.
4. **No clothing, and no material distinction at all.** One pink/red mottled
   material covers the entire body. There is no skin-vs-fabric-vs-metal
   read, which is most of what hand-painted character art *is*.
5. **Head framing.** The rig cannot even be evaluated above the neck from
   the delivered captures.
6. **Environment.** Flat grey ground plane against a flat blue gradient sky,
   versus Lost Crown's layered painted parallax. The wood beam is the only
   prop that survives as recognizable.
7. **Shadow artifacts actively subtract.** The jagged static shards read as
   a rendering bug, not art direction — disqualifying in a shipped frame
   regardless of character quality.

This remains the ceiling of the procedural-primitive approach, exactly as
the project's own docs acknowledge. It is not a tuning problem.

### Ranked issues remaining

1. **Cast shadow is not the character's** — static across a full half-cycle,
   non-humanoid, jagged. Highest-severity *functional* defect remaining; a
   rendering bug visible in every frame.
2. **Test camera is head-on to the run axis.** This is what made round 1's
   false positive credible, and it still suppresses the one thing this scene
   exists to demonstrate. Move to a 3/4 or profile view.
3. **Capture stride aliases against the clip length.** A 16-frame stride
   against a 33-frame loop yields 0.485 cycle/sample — near-worst-case.
   3 of the 5 frames delivered are redundant. Sample every 4-6 frames
   (8+ samples per 0.55s loop) so a reviewer sees the cycle, not two poses.
4. **Head still cropped from every capture** (round-1 issue 3, unfixed).
5. **Unblended shoulder/hip capsule joints**; arms detach visually at
   antiphase. Cosmetic but immediately obvious.
6. **Stone/ground shader shows no variation** at test camera distance.

### Recommendation

Round 1's blocker is closed and was never real — credit for finding that,
and the fps cap plus the `CaptureTestShot.cs` removal are correct fixes.
But **do not treat this round as verification passed**: the harness still
produces 3 redundant frames out of 5 and still shoots down the run axis, so
it would fail the same way on the next animation regression. Fix the camera
angle and the sample stride *before* the next round — both are cheap and
they are what makes every future capture reviewable. Then chase the shadow
bug, the only remaining genuine rendering defect. Leave visual fidelity at
2/10 and stop spending on it; that gap is architectural, not parametric,
and no shader tuning closes it.
