# Critic round 5 — after the reachability audit

The previous round found the code-side backlog empty and said further gains
needed art, not engineering. That was true of the *polish* backlog and wrong
about the *content* one: asking a different question — "is everything
implemented also reachable?" — found five things the game promised and did not
have.

## Movement feel: 8/10 (unchanged)

Input latency still 0 frames. Zero deaths across a 340-frame run, holding the
gain the difficulty ramp made in round 4.

**Frame time, measured the controlled way** (one screenshot at the end of a
340-frame run, so capture cost cannot contaminate the sample):

| p50 | p95 | p99 | max |
|---|---|---|---|
| 16.67 | 17.01 | 17.25 | 22.49 |

Flat at the 60fps cap, with everything added since round 4 — spike hazards,
climbable wall bodies, a second wall course, torch lights with per-frame
flicker, parallax layers. None of it costs measurably.

Note for whoever reads the raw capture instead: the same run sampled *with*
periodic screenshots reports p99 40.92 / max 47.69. Those are the capture, not
the game. Filtering "shot frames" out by index does not work — the cost bleeds
across neighbouring frames. Measure with `--shot-every` set past the frame
count, or do not quote the number.

## Content completeness: this round's actual subject

Five promises the game made and did not keep, all now real:

| Was | Is |
|---|---|
| `WallJump` implemented, granted by nothing, no collidable surface anywhere | `WallShaft` chunk with climbable walls and a pickup before it |
| `ChargeAttack` a flag with a HUD icon and no mechanic | hold-to-charge heavy: 2.2x damage, 1.8x knockback, verified 25 → 55 |
| `PhysicsLayers.Hazard` a constant with nothing on it | `SpikeHazard`, 15 damage on a 0.8s cooldown, hurts enemies too |
| `PhysicsLayers.Pickup` declared while pickups sat on layer 0 | pickups occupy their own layer |
| `SaveData.WorldFlags` never written, never read | removed |

Two of the game's four abilities were decorative. A player could have earned
the wall-jump icon and found nothing in the world to wall-jump on.

## Visual fidelity vs Lost Crown: 6/10 (unchanged)

No visual work this round. The standing gap is unchanged and still not
engineering: no particles, no weather, kit art rather than authored art
direction, and a generic run cycle.

## What the gate caught

Adding the spike hazard broke the "enemy can hurt the player" check, because
spikes damage enemies too and the test cached a reference to a target that then
died and was freed — an exception every frame, 28,000 frames, never reaching
its own verdict. The game was right; the test was brittle. First time in this
project the gate stopped a regression before it was reported as done.

## Recommendation

The reachability audit is complete for everything currently declared. Ask the
question again on the next flag, layer or state added. What remains is design:
the game cycles three room layouts forever with no arc and no ending, and that
is a decision for whoever owns the product, not a defect to fix unprompted.
