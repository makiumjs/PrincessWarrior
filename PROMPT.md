# Prompt di sviluppo — 2.5D Action-Roguevania (Godot / C#)

Build a 2.5D action-platformer in Godot 4.x using C# (.NET), in the spirit of
Prince of Persia: The Lost Crown and Dead Cells — side-scrolling camera, 3D
character and geometry constrained to a movement plane (CharacterBody3D locked on
the Z axis, classic Godot 2.5D setup), precise acrobatic movement (all 4 abilities
unlocked natively: double jump, dash with short i-frames, wall jump, charge attack),
combo-based melee combat with 3-channel telegraph parry (standard, critical, unparryable),
and a Roguevania run loop: 10 procedural rooms generated via mathematical metric
envelopes, 3-act biome progression with branching forks, checkpoint shrines, and
persistent shortcuts unlocked via world levers.

Art & Audio: CC0 KayKit rigged 3D models with runtime-blended skeletal animations,
PBR environment materials with dynamic lighting, procedural SFX combined with
CC0 Vorbis atmospheric act beds. Real-time deterministic 60 FPS performance.

Build/iteration note: C# in Godot requires a build step (dotnet build / Godot's
internal C# rebuild) before each run — every /loop round pays this cost. Accepted
trade-off for C#'s typing and performance over GDScript's instant reload.

## STEP 1 — Before any fan-out: write ARCHITECTURE.md

Define subsystem boundaries — render/camera, player movement, combat, world/level
(interconnected scene graph + ability gates), physics/collision, enemy AI, UI/HUD,
save/checkpoint system, audio, procedural art generation — each subsystem's public
interface (C# namespaces/classes), scene/directory ownership, cross-subsystem signal
vocabulary (Godot signals), shared types (structs/resources). This is the contract
every subagent works against.

Flag explicitly: player movement + physics/collision + camera are the coupled trio
here (equivalent to tonemapping/sky/lighting in an FPS) — jump arc, dash distance,
wall-jump timing, and camera framing all depend on each other frame-to-frame. Give
these a single owner even though the rest of the subsystems fan out in parallel.

## STEP 2 — Fan out subagents

Assign each remaining subagent one subsystem directory/scene tree from
ARCHITECTURE.md. /loop each subagent on its own subsystem, self-testing against the
shared contract.

## STEP 3 — Critic loop, per subsystem, every iteration

A separate subagent — not the one that built the feature — reviews it. Build the
capture tooling first: a headless Godot run (`godot --headless`) that drives a
scripted playtest and dumps frame screenshots (`get_viewport().get_texture()
.get_image().save_png()`) plus frame-time data (`Performance.get_monitor(...)`,
attributed p50/p95/p99, not just an average) to disk for the critic to read.

For a platformer, game feel outranks visuals: the critic must specifically check:

- Input-to-action latency (measured, in ms — jump, dash, attack), not eyeballed.
- Movement parameters against reference: jump height/arc, dash distance and
  cooldown, coyote-time window, input-buffer window, wall-jump angle/impulse.
- Combat responsiveness: hit-stop, combo cancel windows, i-frame duration on dash.
- Visual fidelity 1-10 blind side-by-side vs Lost Crown reference footage, with
  written justification, scored separately from the feel checks above.
- Actual profiled frame time (p50/p95/p99, hitches attributed by cause — shader
  compile, GC pause, physics step) during real movement/combat, not an idle scene.
- Stop condition per subsystem: score plateaus (±0.2) for 2 consecutive rounds,
  OR reaches your target threshold (e.g. 7/10 on both feel and visuals), OR a
  fixed round budget (e.g. 5 rounds) is exhausted. Report the trend honestly —
  do not claim parity with the real game if the score never gets there.

## STEP 4 — Integration pass (sequential, single owner)

One agent, one owner, resolves cross-subsystem regressions on the movement/physics/
camera trio after the parallel round lands. Re-run the critic loop on the result.

Deliverable per round: updated code, updated ARCHITECTURE.md if interfaces changed,
critic scores (feel + visual, separately) with justification, measured input
latency, profiler numbers. Report trend across rounds.
