# Critic round 3 — the assembled game (Main.tscn)

First review of the game as a whole rather than isolated subsystem test scenes.
Everything below is measured from a 330-frame scripted run of `scenes/Main.tscn`
(`docs/critic-captures/round3/`), not inferred from code.

## Movement feel: 7/10

**Input latency: 0 frames on all six scripted jumps** (pressed 40/66/144/196/274/300,
`Jumped` fired on the same frame each time). This is the strongest number in the
project and it has held across every round since the camera fix.

**Frame time**: p50 16.66ms, p95 26.21ms, p99 32.06ms, max 45.66ms.
Caveat that matters: the capture harness pins `Engine.MaxFps = 60`, so p50 =
16.66ms is the cap, not the engine's headroom — this measurement cannot say how
much performance margin exists. The p95/p99 spikes land on the screenshot
frames (`--shot-every=55`), i.e. capture cost, not gameplay cost.

**Against it**: the run recorded `PlayerDied` twice in 330 frames (~5.5s). The
first gap is sized at SafeGap 2.44m against a MaxJumpRun of 3.26m, so it is
provably clearable — but with `UnlockedAbilities = 0` and imprecise timing it
punishes hard, immediately, before the player has learned anything. A first
room should teach, not execute. The metric contract guarantees fairness in
principle; it does not yet grade difficulty across the run.

Also observed: `EnemyDied` at frame 267 with no preceding `EnemyDamaged` — a
chasing enemy walked off a ledge and hit its own fall-death plane. Intended
(aggressive pursuit), but it means encounters can delete themselves.

## Visual fidelity vs Lost Crown: 4/10

Up from 2/10 at the procedural-capsule stage. What earns it: a real rigged
character with weapon and a run cycle, a coherent CC0 dungeon kit at a matching
scale, textured floors/walls, props, and readable side-on orthographic framing.

What keeps it at 4 against Lost Crown:
1. **No background depth.** Behind the wall is flat grey. Lost Crown layers
   parallax planes; here the world is one wall thick.
2. **Flat lighting.** Two directional lights, no atmosphere, no warm/cool
   contrast, no local light sources despite the kit shipping torches.
3. **Upper third of the frame is empty.** The lower wall course fixed the void
   below the floor this round; above the wall there is still bare background
   where a dungeon needs ceiling or darkness.
4. **Nothing animates in the environment** — no banners moving, no particles,
   no dust.
5. The checkpoint prompt never clears once respawns start re-triggering it.

## Recommendation

Highest-value next passes, in order: (1) difficulty ramp — the first room must
be survivable without abilities; (2) parallax background layers; (3) lighting
pass with torch point-lights from the kit. None of these are code defects; all
three are the difference between "systems work" and "looks like a game".
