## Round 1 — 2026-09-04

**Combat feel: 4/10**

Tunables in `CombatController.cs` are individually sane: `LightAttackDamage=10`/
`HeavyAttackDamage=25` with `ComboDamageStepBonus=2` gives a light combo that
escalates 10→12→14, a normal metroidvania spread; `ComboWindowMs=400` and
`CancelWindowMs=250` are standard buffer/cancel windows; `HitStopDurationMs=80`
is a reasonable "chunky but not stunned" hit-stop; per-swing windup/active/
recovery (Light 90/110/180ms, Heavy 200/150/350ms) reads as a credible
light-fast/heavy-slow split.

Input-to-hit latency from `events.csv` (frame × ~16.7ms) vs the script
(`10:attack_light,40:attack_light,70:attack_heavy`): first light attack
pressed frame 10 → `EnemyDamaged` fires frame 19 (9 ticks/~150ms — plausible,
close to Light's 90ms windup plus ~2 ticks of Area3D overlap-detection lag).
The second hit is the anomaly: the frame-40 press buffers into combo step 1
(console confirms `begin Light attack, combo step 1` right after the press),
so by the same windup math it should land ~frame 47-49, but `EnemyDamaged`
doesn't fire until frame 72 — 32 ticks/~533ms from press to hit, 3.5x the
first hit's latency, for the identical Light windup/active budget. Nothing
in the read code explains this gap on its own; `TriggerHitStop()`'s global
`Engine.TimeScale=0.05` dip (documented in the class comment as affecting
"background systems") also scales the *attacker's own* `_PhysicsProcess`
delta, since Godot scales physics delta by TimeScale — during the ~80ms/
~5-tick hit-stop window after hit 1, Combat's own phase timers nearly freeze
too. That accounts for maybe 5 ticks of the 23-tick unexplained delta, not
all of it — worth root-causing, not confirmed as the sole cause.

Third finding, also measured not eyeballed: the heavy attack (pressed frame
70, confirmed beginning around frame 73 per console log) never produces a
third `EnemyDamaged` row — `events.csv` has exactly two hit events (frames
19, 72) for three scripted attacks. Heavy's windup+active (200+150ms = 21
ticks) fits comfortably before the capture ends at frame 120, and
`frame_0120.png` shows the enemy capsule still standing directly next to the
player at melee range — so this reads as a real whiff/bug, not a
range-timeout or capture-cutoff artifact.

`frametimes.csv`'s `wall_frame_ms` column is credibly noisy this round (no
quantization) with the expected every-8-frame capture-overhead bump (~26-29ms
on frames 9/17/25/.../113, matching `--shot-every=8`) plus one large frame-2
startup spike (1154ms) — both consistent with the pattern playercamera
already confirmed as normal. `process_time_ms`/`physics_time_ms` are still
the same broken profiling hook flagged in both playercamera rounds: flat at
exactly `0.421`/`1.966` for all 120 rows, zero jitter — still unfit as
evidence, still not fixed.

**Visual fidelity: 3/10**

Same disclaimer as playercamera: bare capsules (white player, red enemy) on
flat grey blockout, ProceduralArt not integrated, not a real comparison to
Lost Crown. Scored on what IS this subsystem's job, though: hit feedback.
Across frame_0016/0024 (pre/post hit 1) and frame_0072/0080 (post hit 2) the
enemy capsule shows no visible reaction whatsoever — no flash, no stagger,
no discernible knockback despite `LightKnockback=3` being computed and passed
in `DamageInfo` on every hit. Screen-space position of the red capsule drifts
by only a few pixels total across the entire 120-frame sequence (visually
confirmed across all 5 sampled frames), which does not read as "hit" at all.
For a combat-logic testbed this is the one visual signal that matters and
it's currently absent.

**Specific issues found (ranked by severity):**
1. Heavy attack (3rd scripted input) produces no `EnemyDamaged` event despite
   the enemy remaining in range through frame_0120 and ample windup+active
   time elapsing — verified via `events.csv` (only 2 rows) and the PNG. Needs
   root-causing before combo-finisher damage can be trusted.
2. Second hit's input-to-hit latency (32 ticks/~533ms) is 3.5x the first
   hit's (9 ticks/~150ms) for an identical Light attack — measured via
   `events.csv` vs script frames, not fully explained by the ~5-tick
   hit-stop TimeScale dip alone; worth instrumenting further.
3. No visible knockback/hit-reaction on the enemy capsule across either
   landed hit, despite `DamageInfo.Knockback` being computed and applied —
   verified by comparing pre/post-hit PNGs pixel-by-pixel. Hits currently
   don't *read* as hits.
4. `process_time_ms`/`physics_time_ms` remain the same broken flat/quantized
   columns flagged in playercamera rounds 1-2 — still not fixed, still dead
   weight in the CSV; `wall_frame_ms` is the only trustworthy timing column.

**Recommendation:** Root-cause the missing heavy-attack hit (issue 1) first —
a combo finisher that can silently whiff is the most severe functional gap.
Then investigate the hit-stop TimeScale interaction with Combat's own phase
timers (issue 2) — either confirm it's the full explanation for the latency
gap or find the real cause. Issue 3 (no knockback feedback) is a feel/juice
gap, not a blocker, but should land before combat is called done. Issue 4 is
a cross-subsystem fix (shared profiling hook) — same ask as playercamera's
outstanding recommendation, now confirmed broken in a second subsystem's
capture, which raises priority on fixing it centrally rather than per-scene.

## Round 2 — 2026-09-04

**Combat feel: fixed (was 4/10).** Two stacked bugs, both confirmed by trace:
1. Hit-stop dips `Engine.TimeScale` to 0.05, and Godot scales the delta given
   to `_PhysicsProcess` by it — so Combat's own Windup/Active/Recovery
   countdowns stretched ~20x during each dip. Measured pre-fix: light attack
   Active ran 12+ physics frames instead of its window; the follow-up attack's
   windup took ~29 frames instead of ~5 (this WAS the unexplained 150ms vs
   533ms variance); the heavy never started because the prior attack was still
   resolving (the "silently misses" finding). Fixed: Combat divides its delta
   by `Engine.TimeScale` — hit-stop freezes the world, not the attack schedule.
2. `CombatTestDriver` called `GetTree().Quit()` at a fixed frame, truncating
   captures before the heavy could resolve. Removed.
**Evidence:** all 3 scripted attacks now land (10 + 12 + 29 damage, `EnemyDied`
at frame 84). Latencies now match configured windups: light 5 frames ≈ 83ms
(`LightWindupMs=80`), heavy 14 frames ≈ 233ms (`HeavyWindupMs=200`).

## Round 2 (independent verification) — 2026-09-04

**Combat feel: 6/10** (was 4/10)
**Visual fidelity: 3/10** (unchanged)

### The two headline bugs are really fixed

`events.csv` has 5 rows across a 160-frame run: `EnemyDamaged` at frames 15,
48, 84, plus `EnemyDied` at 84. Three scripted attacks, three landed hits —
round 1's "heavy silently whiffs" is gone. `CombatTestDriver.cs:97-101` now
carries an explicit comment where `GetTree().Quit()` used to be, and the
capture reaching frame 160 (`frametimes.csv` = 160 data rows, `frame_0160.png`
present) confirms the truncation is gone. Damage math checks out against the
code without needing the console log: `BasicMelee` has `MaxHealth=30`
(`EnemyController.cs:41`), and steps 0/1/2 of `ApplyHit` yield 10 + 12 + 29 =
51, so death on the third hit is arithmetically forced.

Latency, computed at 16.667ms/tick (no `physics_ticks_per_second` override in
`project.godot`; `CaptureRunner` logs press frames and events off the *same*
`_frameCount` physics counter, so the CSV clock is exact):

| attack | press | hit | ticks | ms | predicted by code |
|---|---|---|---|---|---|
| light (opening) | 10? | 15 | 5 | 83 | **7 ticks / 117ms** |
| light (combo 1) | 40 | 48 | 8 | 133 | 8 ticks / 133ms, matches |
| heavy (combo 2) | 70 | 84 | 14 | 233 | 14 ticks / 233ms, matches |

Prediction model, straight from the source: a buffered swing costs 1 tick
(`HandleInput` sets `_bufferedValid`; `UpdatePhase` consumes it the *next*
frame — `UpdatePhase` runs before `HandleInput` in `_PhysicsProcess`), plus
ceil(windup / 16.667) ticks, plus 1 tick for the Area3D to populate overlaps
after `Monitoring = true`. Light = 1+6+1 = 8. Heavy = 1+12+1 = 14. Both
buffered attacks land on the predicted frame exactly. That is a genuine,
independently reproducible match — the timing subsystem is now correct.

### Where the round-2 write-up overstates it

1. **`LightWindupMs` is 90, not 80.** `CombatController.cs:61`, with no
   override anywhere in `CombatTestScene.tscn` (checked the whole scene file).
   The claim "light 5 frames ~ 83ms (`LightWindupMs=80`)" is self-
   contradicting: 83ms is *shorter* than the configured 90ms windup, which the
   phase timer cannot produce. With a frame-10 press the first hit should land
   at frame 17, not 15.
2. **The two lights are not identical: 5 ticks vs 8 ticks, 83ms vs 133ms.**
   One tick of that is structural (buffered swings start a frame later) and
   defensible. The other ~2 ticks are unexplained. The entry quoting only
   "light 5 frames" quietly drops the second measurement — the same class of
   omission round 1 was written to prevent.
3. **The capture has no provenance.** `combat-round2/` contains no console log
   and no record of the `--script` argument, so the press frames (10/40/70)
   cannot be verified from the artifacts at all; I had to take them on faith
   and back-solve. `CaptureRunner` should dump its argv into the out dir.

### Is `delta / Engine.TimeScale` the right fix? Partly.

Measured from `frametimes.csv`, the hit-stop signature is unmistakable and
identical after all three hits:

- hit 15 -> frame 16 = **55.589ms** wall, 17 = **0.678ms**, 18 = **0.395ms**
- hit 48 -> frame 49 = **62.553ms**, 50 = **0.653ms**, 51 = **0.422ms**
- hit 84 -> frame 85 = **57.837ms**, 86 = **0.612ms**, 87 = **0.388ms**

So `Engine.TimeScale = 0.05` does not merely shrink the delta — it *throttles
the physics tick supply*. Each dip stretches one tick to ~56-63ms of wall time
and then Godot burns two ~0.5ms catch-up ticks when the restore timer fires.
Three physics ticks span each ~80ms dip. Combat now credits all three with a
full 16.667ms (50ms total) against ~58-64ms of real time, so the attack
schedule loses only ~8-14ms per hit-stop instead of round 1's near-total
freeze. The fix works. But note *how*: two ticks that consumed 1.0ms of wall
time advance the swing by 33ms. **After every landed hit the attack schedule
lurches ~2 frames forward in ~1ms of real time.** Invisible today because
nothing animates. The moment an `AnimationPlayer` drives the swing it will
still be `TimeScale`-scaled while the hitbox window is not, and the two will
disagree by 2-5 frames per hit. That mismatch is designed in and needs a
decision before animation lands.

Two further objections to the mechanism itself:

- `Engine.TimeScale` is global and shared. Combat now exempts itself from
  *any* time scaling, not just its own hit-stop. A slow-mo ability, a boss
  intro ramp or a debug slowdown would leave the player swinging at full speed
  straight through it. The correct primitive is a hit-stop Combat owns (an
  unscaled `Time.GetTicksUsec()` delta, or an explicit `_hitStopRemaining` it
  skips), not division by a global.
- `Mathf.Max((float)Engine.TimeScale, 0.0001f)` (line 163) is a landmine. At
  `TimeScale = 0.001` the clamp never engages and one tick advances the attack
  by 16.7 *seconds* — every phase completes in a single frame. It guards
  against divide-by-zero, not against the actual failure mode.

### Knockback (round 1 issue 3): still open, untouched.

Nothing in this round's fix went near it. The path exists end to end —
`CombatController.cs:315` computes it, `EnemyController.cs:179` stores it,
`:255-259` applies `velocity.X = _pendingKnockback.X` on the first Stagger
tick — but the magnitude is the problem: `LightKnockback = 3` decaying via
`MoveToward(velocity.X, 0f, 10f * dt)` stops in 0.3s, ~0.45m total, after
which Chase walks the enemy straight back. Across `frame_0040`, `frame_0080`
and `frame_0120` the red capsule sits within a few pixels of the same screen
position and never moves *right* (away from the player) at all. There is also
no hit flash anywhere — no material, modulate or albedo change exists in
`EnemyController`. Hits still do not read as hits.

Worse, and new this round: **the killing blow produces no reaction whatsoever.**
`TakeDamage` calls `Die()` and returns early (`EnemyController.cs:173-177`), so
`_pendingKnockback` is never set and Stagger is never entered on the fatal hit.
And `Die()` (`:290-297`) only sets state, zeroes velocity, clears collision
layers and emits — no despawn, no topple, no fade. `frame_0120` is 36 ticks
(600ms) after `EnemyDied`, `frame_0160` is 1.3s after, and in both the enemy is
still standing upright next to the player, identical to when it was alive. A
corpse indistinguishable from a live enemy is a worse visual state than round 1
documented.

### Ranked issues

1. **No hit reaction, no death reaction.** Knockback ~0.45m then walked back,
   no flash, fatal hit skips Stagger entirely, dead enemy stands upright
   indefinitely. Verified across `frame_0040/0080/0120` and
   `EnemyController.cs:173-177, 255-259, 290-297`. Highest-value fix remaining
   and the cheapest — this is what makes combat feel like combat.
2. **`delta / Engine.TimeScale` couples Combat to a global.** Right result
   today, wrong mechanism: it immunizes attacks against *all* future time
   scaling, and the `0.0001f` clamp doesn't cover small-but-nonzero scales.
   Replace with a Combat-owned unscaled clock.
3. **Event ordering inverted on the fatal hit.** `events.csv` logs `EnemyDied`
   at 84 *before* `EnemyDamaged` at 84, because `ApplyHit` calls `TakeDamage`
   (which emits `EnemyDied` from inside) at line 319 before `EmitEnemyDamaged`
   at line 320. Any damage-number, hit-VFX or health-bar listener sees the
   death before the damage that caused it.
4. **Post-hit-stop schedule lurch (~2 frames in ~1ms wall).** Harmless now,
   guaranteed animation desync later. Decide the policy before animations land.
5. **Dead code at line 309.** `if (away.Length() < 0.01f)` can never be true —
   `away.Y = 0.3f` is assigned the line above, forcing length >= 0.3. The
   intended "target exactly overlapping" fallback never fires, so an
   overlapping enemy is knocked straight up instead of in the facing direction.
6. **`TriggerHitStop` restores unconditionally to 1.0.** Two hits close
   together (or one swing hitting two enemies in one frame —
   `ProcessActiveHitbox` loops per body) create independent restore timers; the
   first to fire ends everyone's hit-stop and clobbers any other system's
   `TimeScale`.
7. **Capture provenance missing.** No console log, no argv record in
   `combat-round2/`. Round 2's own latency claims are not checkable from its
   own artifacts.
8. **`process_time_ms`/`physics_time_ms`: improved, still unusable per-frame.**
   No longer frozen at a single constant for the whole run (8.256/19.467 ->
   14.072/39.563 -> 33.535/46.046 -> 18.775/0.387), but held constant in
   ~40-frame blocks. `wall_frame_ms` remains the only per-frame timing column —
   and this round it earned its keep: it's what proved the tick-throttling
   above.

### Stop condition: NOT met.

Threshold is both axes >= 7; actual is 6 and 3. Not a plateau either — visual
feedback has never been worked on, and issue 1 is a small, well-scoped change
(hit flash + larger/longer knockback + stagger on the fatal blow + a death
visual) that would plausibly move both axes at once. One more round, scoped to
issues 1 and 3, is warranted. Issue 2 is a correctness cleanup that can ride
along.

## Round 3 — 2026-09-04 — correction to round 2's knockback finding

**Round 2's "knockback still open, untouched" was WRONG, and so was my repeat
of it.** Knockback was implemented all along: `TakeDamage` sets
`_pendingKnockback` and `TickStagger` applies it on the first tick after
entering Stagger. Measured on a live light hit: enemy X moved 3.133 → 3.442 over
12 physics frames with a proper velocity decay (2.86 → 1.66 m/s). It was not
visible in round 2's evidence because the sampled frames were 20 apart and the
displacement is ~0.45 m — a handful of pixels at that camera distance. Reading
the code alone was not enough to conclude it was missing, and neither was
re-stating it without measuring.

**What WAS genuinely broken, and is now fixed:** a *fatal* hit returned from
`TakeDamage` before `_pendingKnockback` was ever assigned, so the killing blow
— the one that should land hardest — applied zero impulse; `Die()` then set
`Velocity = Vector3.Zero` and `_PhysicsProcess` bailed out early on Dead, so
the corpse froze at the instant of death. Now the impulse is recorded before the
death check, `Die()` keeps it, and a `TickDead` integration step decelerates the
body and applies gravity so it slides out and settles.
**Evidence:** fatal heavy hit records kb=(5.75, 1.72) where it previously
recorded (0,0); frames 90 → 110 show the corpse displaced clear of the player
instead of dropping in place.
