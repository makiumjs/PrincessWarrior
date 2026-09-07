# ARCHITECTURE.md — LostCrownlike

Contract every subagent builds against. Engine: Godot 4.7.2 (.NET/C#, net8.0).
Project root: `game/`. This document is the source of truth for subsystem
boundaries, ownership, signals, and shared types — update it whenever an
interface changes, before writing the code that changes it.

## Directory map

```
game/
  src/
    Core/            shared types, autoloads, no subsystem owns gameplay here
    PlayerCamera/     <- coupled trio, single owner (movement + physics + camera)
    Combat/
    World/
    AI/
    UI/
    Save/
    Audio/
    ProceduralArt/
  scenes/
    player/
    world/
    ui/
```

## The coupled trio: PlayerCamera

Player movement, physics/collision response, and camera framing are one
subsystem with one owner — not three. Jump arc, dash distance, wall-jump
impulse and camera dead-zone all change together; splitting them across
agents caused the cross-system regressions documented in the reference
project (Claude-of-Duty) on tonemapping/sky/lighting. Do not split this.

**Directory:** `src/PlayerCamera/`
**Owns:** `CharacterBody3D` locked to the X/Y plane (Z axis frozen), all
movement states (Idle, Run, Jump, DoubleJump, Fall, Dash, WallSlide,
WallJump, Hurt), input buffering, coyote time, and the `Camera3D` rig
(side-scroll follow, look-ahead, dead-zone, framing during dash/combat).

**Public interface (`PlayerController.cs`):**
- Properties: `MovementState CurrentState`, `Vector3 Velocity`, `AbilityFlags UnlockedAbilities`
- Signals (see Event vocabulary below): `Jumped`, `DoubleJumped`, `Dashed`,
  `WallJumped`, `Landed`, `Damaged(DamageInfo info)`, `Died`

**Tunable movement parameters** (must be exposed as `[Export]` fields, not
hardcoded, so the critic loop can read/tune them and log them per round):
`JumpHeight`, `JumpApexTime`, `DashDistance`, `DashDuration`, `DashCooldown`,
`DashIFrameDuration`, `CoyoteTimeWindow`, `InputBufferWindow`,
`WallJumpImpulse`, `WallJumpAngleDegrees`. Implementation also exposes
additional `[Export]` fields needed to make the above actually produce a
playable controller (ground speed/accel/friction, air control, fall-gravity
multiplier, jump-cut multiplier, wall-slide speed, hurt stun/i-frames/
knockback, max health) — these are additive, not replacements.

**Implementation notes (`PlayerController.cs`):**
- `Velocity` is the `Vector3 Velocity` CharacterBody3D already provides —
  not redeclared.
- `Gravity`/jump takeoff velocity are derived from `JumpHeight` +
  `JumpApexTime` each `_Ready` (`h = 0.5*g*t²`), so those two exports stay
  the single source of truth for jump feel.
- `UnlockedAbilities` defaults to `DoubleJump | Dash | WallJump` (not
  `None`) so the controller and its test scene are exercisable standalone
  before World/Save exist. It also subscribes to `EventBus.AbilityUnlocked`
  and ORs in newly granted flags at runtime — World/Save can still drive it
  normally; a full new-game flow should reset it from `SaveData` explicitly.
- Implements `Core.IDamageable.TakeDamage` directly (the documented
  exception to the "only EventBus" rule) and is added to the `"player"`
  Godot group in `_Ready`, matching the convention `src/AI/EnemyController.cs`
  already relies on for its group-based, compile-time-decoupled player
  lookup.
- Additive public property beyond the documented interface: `int
  FacingSign` (+1/-1), read by `SideScrollCamera` for look-ahead and
  available to Combat/UI for hitbox/HUD facing.

## Subsystems (fan out in parallel, one owner each)

### Combat — `src/Combat/`
Combo input buffering and cancel windows, hit-stop, hitbox/hurtbox via
`Area3D`. Any damageable node implements `Core.IDamageable`. Reads player
state from `PlayerController` (does not modify it). Reports its own tunables
as `[Export]` fields: `ComboWindowMs`, `HitStopDurationMs`, `LightAttackDamage`,
`HeavyAttackDamage`.

**Implementation notes (`CombatController.cs`):**
- Add as a child `Node3D` of the player `CharacterBody3D`
  (`PlayerController`) — see `scenes/player/PlayerTestScene.tscn`'s
  `Player/CombatController` node and the standalone
  `scenes/CombatTestScene.tscn`. Resolves its `PlayerController` via
  `GetParent()`, falling back to an explicit `[Export] NodePath PlayerPath`
  or the `"player"` group. Reads `CurrentState` (blocks starting a new
  attack during Dash/Hurt/WallJump/WallSlide or at 0 `CurrentHealth`) and
  `FacingSign` only as a knockback-direction fallback; never writes either.
- Attack state machine: `Windup -> Active -> Recovery`, timed per attack
  type (`LightWindupMs`/`LightActiveMs`/`LightRecoveryMs`, and a `Heavy*`
  set), additive tunables beyond the four required ones, same pattern as
  PlayerCamera's "extra" exports. A 3-hit combo (`ComboMaxSteps`) advances
  on a buffered attack press; the press is remembered (`_bufferedValid`)
  from the moment Active begins through `ComboWindowMs` after Recovery
  starts, and is consumed immediately (canceling the rest of Recovery)
  rather than waiting out the full recovery pose — this is the "cancel
  window" for chaining combo hits. A separate `CancelWindowMs`, measured
  from Active's start, governs canceling out of an attack into a dash: the
  controller subscribes to `EventBus.Dashed` (emitted by `PlayerController`
  — Combat never calls into it) and resets its own attack state if the dash
  fires inside that window. `PlayerController`'s dash is never gated by
  Combat either way; this only clears Combat's bookkeeping so a dashing
  player isn't stuck "in recovery."
- Hitbox: a single `Area3D` + `BoxShape3D`, built in code in `_Ready()` (no
  scene authoring needed), parented under `CombatController` and offset by
  `HitboxOffsetX`/`HitboxOffsetY` (`[Export] Vector3 HitboxSize` sizes the
  box). Because `PlayerController` faces left/right by rotating the whole
  body `RotationDegrees.Y` between 0/180 rather than mirroring a child, a
  local `+X` offset here inherits that rotation and lands on the correct
  side automatically — no `FacingSign` multiplication needed for placement.
  `CollisionMask = PhysicsLayers.Enemy`; monitoring is enabled only during
  the Active phase (per-swing `_hitThisSwing` set prevents multi-hit).
  Overlaps are polled via `GetOverlappingBodies()` each physics frame
  during Active rather than via the `BodyEntered` signal, since a body
  already standing in the box when `Monitoring` flips true is not
  guaranteed to fire an "entered" signal that same frame. On a hit:
  `IDamageable.TakeDamage(DamageInfo)` (`LightAttackDamage`/
  `HeavyAttackDamage` + a small flat per-combo-step bonus) then
  `EventBus.Instance.EmitEnemyDamaged(enemy, info)`.
- Hit-stop: a global `Engine.TimeScale` dip (to `0.05`, not a hard `0`, to
  avoid physics-step edge cases) for `HitStopDurationMs`, restored via a
  `SceneTree.CreateTimer(..., ignoreTimeScale: true)` callback — chosen
  over per-node pause because a per-node freeze would leave the enemy's own
  hurt/flinch reaction playing at full speed underneath the player's frozen
  swing, which reads as wrong for a "shared moment of impact." Documented
  trade-off: the dip is global, so it also briefly affects unrelated
  systems (e.g. a patrolling enemy elsewhere on screen) for the same short
  window; acceptable at the tens-of-ms durations this is tuned for.
- **Attack pacing uses UNSCALED time (round 2 fix).** `Engine.TimeScale`
  hit-stop dips also scale the delta Godot hands `_PhysicsProcess`, so the
  attack state machine's own Windup/Active/Recovery countdowns were being
  stretched ~20x for the duration of each dip. Measured: a light attack's
  Active phase ran 12+ physics frames instead of its configured window, the
  follow-up attack's windup took ~29 frames instead of ~5 (this was the
  "unexplained 150ms vs 533ms latency variance" the round-1 critic found),
  and a third scripted attack never started because the previous one was
  still resolving (the "heavy attack silently misses" finding). Combat now
  divides its delta by `Engine.TimeScale`: hit-stop freezes the world, never
  the schedule of the attack that caused it. Post-fix latencies are
  consistent and match the configured windups (light 5 frames ≈ 83ms vs
  `LightWindupMs=80`; heavy 14 frames ≈ 233ms vs `HeavyWindupMs=200`).
- Input actions `attack_light` / `attack_heavy` added to project.godot's
  existing `[input]` section (J / left mouse button, and L / right mouse
  button, respectively — merged in alongside the existing bindings, none
  duplicated).
- Enemy-can-damage-player gap: **already closed, not by Combat.**
  `EnemyController.TickAttack` (src/AI/EnemyController.cs, built by the AI
  subsystem) already calls `ApplyAttackDamage` on its cached player-group
  target once windup elapses, via a duck-typed `IDamageable` cast and a
  distance check (`AttackRange`) rather than an `Area3D` hitbox — so combat
  was already two-way before this subsystem started. Combat did not modify
  `EnemyController.cs` (out of scope per this subsystem's boundary) and
  adds no separate enemy-side hitbox; noted here since the AI subsystem's
  approach (distance check, not `Area3D`) differs from the `Area3D` hitbox
  pattern Combat uses for the player's own attacks — a documented
  inconsistency for a future pass to reconcile if the critic loop flags it,
  not a functional gap.
- `scenes/CombatTestScene.tscn` + `src/Combat/CombatTestDriver.cs`
  (TEST-SCENE ONLY, same convention as `src/AI/PlayerStandIn.cs` /
  `EnemyTestSceneBootstrap.cs`): places the real `PlayerController` next to
  a `BasicMelee` enemy, simulates an `attack_light` press via
  `Input.ActionPress`/`ActionRelease` (works headless), and asserts
  `EventBus.EnemyDamaged` fired with the expected `LightAttackDamage`
  amount and that the enemy's own state machine actually reacted
  (`EnemyState.Stagger`, tracked across all frames since it may have
  already recovered to `Chase` by the time the assertion runs) — verified
  end-to-end via a headless run, not asserted from reading the code.

### World — `src/World/`
Scene-based level chunks linked by doors/transitions. Owns static level
collision geometry (separate physics layer from the player body — see
Shared types). Owns the map graph (`WorldGraph.cs`, a `Resource`) describing
room connections and which `AbilityFlags` gate each connection. Does not
touch player movement code; gates abilities by listening to
`AbilityUnlocked` and enabling/disabling `Area3D` blockers.

### AI — `src/AI/`
`EnemyController.cs` base class: state machine (Patrol, Chase, Attack,
Stagger, Dead). Implements `Core.IDamageable`.

**Navigation — corrected (round 2).** The original spec said "navigation via
Godot `NavigationAgent3D`". That does not work for this game and has been
replaced by direct X-axis steering toward an explicit `_steerTarget`.
Reason, measured not assumed: Godot's 3D navmesh pathing is built for
horizontal (XZ) walkable surfaces, but this is a 2.5D side-scroller whose
movement plane is X/Y with Z locked. The baked navmesh never mapped the
patrol targets onto itself, so `GetNextPathPosition()` returned the agent's
*own* position on every tick (traced at pf=40/80/120/160: `next==pos`,
`dx=0`), and since `NavigationAgent3D` only recomputes a path when
`TargetPosition` changes — and the target never changed because the enemy
never arrived — the enemy stayed frozen permanently (all 8 sampled capture
frames byte-identical). A navmesh also cannot express what a platformer
enemy needs anyway: ledges, drops, jump arcs. `NavigationAgent3D` is still
present on the node for arrival bookkeeping and possible future use, but
movement no longer depends on it.

**Two seams, added for the boss.** `AbsorbDamage(int)` and
`StaggersOnHit(DamageInfo)` are identity and `true` for every normal enemy.
They exist so a type can be armoured without duplicating the death and
knockback path, which is where the bug would live: the killing blow's impulse
was already lost once in this file by an early return, and the fix reads as
obvious only in hindsight.

### UI — `src/UI/`
HUD (health, unlocked-ability icons, checkpoint prompt, boss bar), title
screen, pause menu. Pure consumer of signals — never calls into gameplay
subsystems directly, only listens on the `EventBus` autoload.

The boss bar is why `BossStateChanged` exists on the bus at all. Letting the
HUD find the boss node would have been three lines instead of a signal and a
handler, and would have made the HUD the one place in the project that knows
a gameplay type. The title screen reads `InputMap` rather than a typed list
for the same reason in reverse: the bindings have one home, and a second copy
of them is a label that keeps naming the old key.

### Save — `src/Save/`
`SaveManager.cs` autoload. Persists `SaveData` (see Shared types) to
`user://`. Listens for `CheckpointReached`, `AbilityUnlocked` and
`PlayerDied`.

**Checkpoint respawn (round 2 fix).** Round-1 critics on Save and World
independently found the same gap from opposite ends: `SaveData.PlayerPosition`
had no producer anywhere in the codebase, and `LevelManager`'s checkpoint
position bookkeeping had no consumer — so a reload restored abilities and a
checkpoint id but never put the player anywhere. `SaveManager` now captures
the player's `GlobalPosition` (found via the `"player"` group, no compile-time
dependency on PlayerCamera) when `CheckpointReached` fires, persists it, and
restores it on `PlayerDied` via `RestorePlayerPosition()`. Uses only existing
EventBus events — no new event vocabulary. Verified end to end: checkpoint at
(12.5, 3.25, 0) → player moved to (-99,-99,0) → `PlayerDied` → player back at
(12.5, 3.25, 0), and the same value re-read from disk.

### Audio — `src/Audio/`
`AudioManager.cs` autoload. Procedural SFX via `AudioStreamGenerator`
(no sample files) triggered by `EventBus` signals (jump, dash, hit, land).

**Recorded atmosphere, and the one place PROMPT.md's "no external asset files"
no longer holds.** It had already stopped holding for art, which is KayKit.
`game/assets/audio/` carries seven CC0 Vorbis tracks and `AudioManager` plays
them in two tiers:

- **The bed** says which ACT you are in -- `BedForRoom` divides the run into
  thirds, so the mapping survives `RunLength` being retuned, and `RunLength` has
  already moved once. `boss_theme` outranks the act while the Warden lives.
- **The layer**, quieter, says what the ROOM is: `RoomShape` classifies a room's
  vertical rise against the player's own jump (`DungeonRoomBuilder.ShapeOf`) and
  Audio maps the resulting word to a file. Audio therefore needs to know nothing
  about level geometry, and World needs to know nothing about audio.

The synthesised drone stands down while a bed plays -- two continuous sources on
one bus is mud, and the drone was always standing in for this. It is still fed
with silence, because an `AudioStreamGenerator` whose buffer is never drained
reads as starved to check 48. The drips stay: they are events, and they are what
keeps a bed from being wallpaper. The procedural MELODY stays too, because it is
the part that answers to the run -- intensity rises room by room -- and a
recording cannot.

Bed streams load with `ResourceLoader.CacheMode.Ignore`. Cached, they outlive
the players that release them and the engine reports "1 resources still in use
at exit" -- measured 3 boots of 3 with beds on, 0 of 3 with them off, and
clearing the player's `Stream` in `_ExitTree` did not touch it, because the
cache was the owner.

### ProceduralArt — `src/ProceduralArt/`
`ProceduralMeshFactory.cs`: builds character and environment geometry at
load time via `ArrayMesh`/`SurfaceTool`. Materials via `.gdshader` files
(no imported textures). Character animation is code-driven procedural
posing (parametrized run/idle/attack poses blended via `AnimationTree`,
built at runtime) rather than hand-authored keyframe clips — this is a
known ceiling versus Lost Crown's hand-painted style (see PROMPT.md); the
critic should score it accordingly, not expect parity.

## Event vocabulary (`Core/EventBus.cs`, autoload singleton)

**Now Godot 4 source-generated signals** (`[Signal] ... EventHandler`), per the
project's Godot/C# contract — no longer plain C# events. Subscribers still use
`+= / -=` unchanged, because the generator emits an event wrapper per signal.

`DamageInfo` payloads are FLATTENED into Variant-compatible parameters
(`int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical`): a
custom struct cannot cross the Variant boundary. The two alternatives were
rejected — turning `DamageInfo` into a `Resource`/`GodotObject` allocates on
every hit, and dropping the fields would lose knockback and crit data that
Combat and AI already consume. `DamageInfo` is unchanged and still used for the
direct `IDamageable.TakeDamage` call, which never goes through the bus.
`AbilityFlags` crosses as `int` and is cast back by each handler.


All cross-subsystem communication goes through this signal bus. No
subsystem calls another subsystem's internals directly except: Combat and
AI read `PlayerController`'s public state (read-only), and everyone may
call `Core.IDamageable.TakeDamage`.

| Signal | Payload | Emitted by | Consumed by |
|---|---|---|---|
| `Jumped` / `DoubleJumped` / `Dashed` / `WallJumped` / `Landed` | — | PlayerCamera | Audio, UI |
| `PlayerDamaged` | `DamageInfo` | PlayerCamera | UI, Audio |
| `PlayerDied` | — | PlayerCamera | UI, Save |
| `EnemyDamaged` | `Node3D enemy, DamageInfo info` | Combat | AI, Audio, UI |
| `EnemyDied` | `Node3D enemy` | AI | World, UI, Audio |
| `AbilityUnlocked` | `AbilityFlags ability` | World | PlayerCamera, UI, Save |
| `CheckpointReached` | `string checkpointId` | World | Save, UI |
| `LevelTransitionRequested` | `string scenePath, string spawnPointId` | World | Core (scene loader) |
| `RoomEntered` | `int index, int total` | World | UI, Audio |
| `BossStateChanged` | `bool alive, float healthFraction, bool armoured, bool enraged` | AI | UI, Audio |
| `RoomShape` | `string shape` -- "flat", "open" or "climb" | World | Audio |

## Shared types (`Core/`)

- `AbilityFlags` — `[Flags] enum { None, DoubleJump, Dash, WallJump, ChargeAttack }`
- `PhysicsLayers` — static class of collision layer bit constants: `Player`,
  `World`, `Enemy`, `Hazard`, `Pickup`. World geometry and player body must
  use these, not magic numbers.
- `IDamageable` — `void TakeDamage(DamageInfo info)`
- `DamageInfo` — struct: `int Amount`, `Vector3 SourcePosition`, `Vector3 Knockback`, `bool IsCritical`
- `SaveData` — `Resource`: `Vector3 PlayerPosition`, `AbilityFlags UnlockedAbilities`, `string LastCheckpointId`, `Dictionary<string,bool> WorldFlags`
- `MovementState` — `enum { Idle, Run, Jump, DoubleJump, Fall, Dash, WallSlide, WallJump, Hurt }`

## Critic tooling (built before Step 3 loops start)

**Correction (verified during the ProceduralArt subsystem's own capture
test, see `docs/proceduralart-test-capture.png`): `--headless` forces
Godot's dummy rendering backend — no Vulkan/OpenGL context is ever
initialized, so `get_viewport().get_texture()` returns a null texture and
any screenshot call throws. This machine has a real GPU (RTX 3080) and a
usable display, and a normal windowed run captures fine — confirmed
working, not assumed.**

`tools/capture/` — a windowed (NOT `--headless`) Godot scripted playtest run
(`godot --path game <scene> -- --capture`) that:
1. Drives a fixed input script (scripted playtest) through a target scene.
2. Dumps frame PNGs via `get_viewport().get_texture().get_image().save_png()`.
3. Logs frame time via `Performance.get_monitor(Performance.TimeProcess)`
   per frame to a CSV, so p50/p95/p99 can be computed offline — not read
   from Godot's live profiler UI.
4. Logs input-to-action timestamps (input received vs. state-machine
   transition) to the same CSV for latency measurement.

Plain `--headless` boot checks (no rendering, just "does it crash on load")
remain valid and are what every subagent above used to verify its own
subsystem — keep using those for compile/crash verification. Only the
critic's visual capture step needs the windowed run.

## Round budget / stop conditions

Per PROMPT.md Step 3: each subsystem's critic loop stops when its score
plateaus ±0.2 for 2 rounds, hits 7/10 on both feel and visual axes, or after
5 rounds — whichever comes first. Track scores in
`docs/critic-log/<subsystem>.md`, one row appended per round.

## Level architecture — Spatial Metric Contract

Rooms are generated, not hand-placed, and every dimension derives from the
player's own movement tunables. Nothing in the level code contains a
hand-guessed distance.

- `src/World/PlayerMetrics.cs` — computes the reachability envelope:
  `AirTime` (rise + a faster fall, since `FallGravityMultiplier` applies after
  the apex, so it is NOT 2x the apex time), `MaxJumpRun`, `MaxJumpDashRun`,
  `MaxJumpUp`, `MaxDoubleJumpUp`. `Safe*` variants apply a 0.75 margin —
  a gap sized at the exact limit is frame-perfect and reads as unfair.
  `VerifyAgainst(player)` warns if the live controller has been retuned away
  from these values, which would silently make rooms unbeatable.
- `src/World/MicroChunk.cs` — `MicroChunkComposer` turns a sequence of
  `ChunkKind` into `PlatformRect`s, `WallRect`s, hazard positions, encounter
  points and ability grants, sizing everything from the metrics above.
  The kinds and what each one is FOR:

  | Kind | Demands |
  |---|---|
  | `Gauntlet` | nothing; breathing room, holds an encounter |
  | `Gap` | a running jump |
  | `DashGap` | jump + dash |
  | `StepUp` | a single jump up |
  | `Chimney` | repeated jumps up a zig-zag |
  | `WallShaft` | wall-sliding up a narrow shaft |
  | `Arena` | a fight, not a traversal |
  | `Spikes` | jumping over a trap on solid ground |
  | `Drop` | nothing; a single step DOWN, free to cross |
  | `Chasm` | a dash across, then a wall climb out -- two verbs in one breath |
  | `Rift` | a drop straight into a gap, with no runway at the bottom |
  | `Sweep` | timing: a flat corridor crossed by blades on a cycle |

  `Sweep` is the one kind whose difficulty is not a distance. Every other entry
  is sized from `PlayerMetrics` -- a gap is 2.44m, a step-up 1.88 -- so the only
  axis the generator had for a later room was a bigger number, and a widened gap
  is the same idea at a different size rather than a new one. A blade cannot be
  sized away, and it stays a question once the player owns every ability.

  It damages rather than blocks, deliberately. A moving platform you must WAIT
  for is a rhythm the traversal bot cannot prove: it holds right and never
  waits, and a bot taught to wait stops being the deliberately clumsy witness
  the whole contract rests on. A blade lets a bad player through with a bruise
  and a good one through clean, so the chunk stays provably crossable while the
  timing is real. A platform that has to be ridden is still open, and it needs
  the bot to learn to wait before the check could mean anything.

  **The player owns all four abilities from the first frame**, so a chunk no
  longer places a pickup and no longer gets downgraded when the player lacks
  its verb. The crystals and the `Allowed` gate are both gone: without
  backtracking a gate is not a locked door you return to, it is a chunk quietly
  made easier until a pickup appears -- and what it bought in practice was a
  class of bug where a room's crossability depended on which layout had been
  dealt before it. That bug was not hypothetical: dealing layouts from a bag put
  early layout 0 at room 4 without the wall jump a layout-1 room used to grant,
  and the traversal bot fell out of the world at 101% of the room.

  `AbilityFlags.All` is now the player's starting state and `SaveData`'s
  default. The `AbilityUnlocked` signal stays -- `SaveManager` still replays it
  on load, and it is how the controller would learn of a grant if one is ever
  reintroduced.
- `src/World/DungeonRoomBuilder.cs` — realises each rect as KayKit visual tiles
  plus **one** `BoxShape3D` collider spanning the whole platform, not one per
  tile: adjacent per-tile colliders create seams the player snags on.

Measured with the current tunables: airTime 0.63s, jump run 3.26m, jump+dash
7.26m; safe gap 2.44m, safe dash gap 5.82m, safe step-up 1.88m. Verified
traversable by a deliberately crude bot (fixed-interval jumps, never
frame-perfect) that advanced 49.7m through a generated room without falling
out of the world.

### Camera
`SideScrollCamera` exports `PitchDegrees` (default 8). A perfectly level
camera — orthographic especially — sees a flat floor exactly edge-on, so the
walkable surface collapses into its 0.15m untextured edge. Note the rig
rewrites its own transform every frame: setting rotation in a `.tscn` has no
effect, it must go through this export.

### Headless capture — NOT possible on this build
The project's contract prescribed
`--display-driver headless --rendering-driver opengl3` for critic captures.
Tested: it does not work. Godot's own `--help` states the `headless` display
driver supports only the `dummy` rendering driver (`opengl3` exists only under
`windows`), so the flags silently fall back to dummy, `get_viewport()
.get_texture()` returns null, and the capture throws `Parameter "t" is null`
with zero PNGs written. Captures must run windowed. Plain `--headless` remains
valid for boot/crash checks and for non-visual tests.

## Resolved: there is one room system

There were two ways a room could exist and only one was used. The authored-scene
half -- `LevelManager` (an autoload), `WorldGraph`, `Room1..3.tscn`,
`AbilityGateBlocker`, `LevelTransitionTrigger` and the async-load check that
exercised them -- has been **deleted**.

It was not merely dead. It was *subscribed to a live signal*, so it kept
receiving real events from the running game and acting on them, and it reached
the shipped game twice:

1. As an autoload it injected `Room1.tscn`'s grey blockout into every scene --
   the stray untextured slab in the dungeon captures.
2. `RoomExitTrigger` announces a generated room as `generated:<index>` so audio,
   UI and save still hear a transition. `LevelManager` handed that string to
   `ResourceLoader`, so **every room change in the shipped game** printed
   `Resource file not found: res://generated:1` while thirty-one checks stayed
   green, because none of them read stderr.

The evidence that it was genuinely self-contained: after deletion the project
compiles with zero errors and zero warnings, and nothing outside it referenced
it except comments. `LevelTransitionRequested` stays -- Save, the HUD and
`DungeonRoomBuilder` are real consumers.

**What was given up.** Async `ResourceLoader.LoadThreadedRequest` room loading,
which was an explicit project requirement, and the check that verified it. Both
only ever applied to authored `.tscn` rooms; generated rooms are rebuilt in
place and have nothing to load. The hitch that async loading would have hidden
was measured and addressed differently: a rebuild costs about 155ms and is now
spread one step per frame, which took frames over 33ms in a played run from 117
to 13-23.

The removed files are archived outside the project so they cannot receive
signals or rot, but the code is not lost.

## The run is finite, and one signal says so

`ComposeRoom` picks a layout with `index % 3`, which by itself never stops.
`RoomExitTrigger` now compares `NextRoomIndex` against `RunLength` (10) and, at
the end, emits `RunCompleted` **instead of** requesting a transition, so
nothing rebuilds underneath the ending.

Two details are deliberate rather than incidental:

**`RunCompleted` is its own signal, not a transition with a special path.**
`LevelTransitionRequested` means "a room changed"; the end of a run is the
absence of a next room. Overloading the transition signal is precisely how the
`generated:1` defect above happened -- a listener took a string that was never
a scene and tried to load it.

**`SaveData.RoomsCleared` is a high-water mark, not the current room.** It is
raised on transition and never lowered, so dying back to an earlier checkpoint
does not move the ending further away. Recording the current index instead
would mean a player who died in room 5 would have to clear rooms 4 and 5 again
to reach the same total.

`RunLength` is an `[Export]` on `DungeonRoomBuilder`, forwarded to each exit at
`SpawnExit`. `RoomExitTrigger` declares its own default of 6, which is a
fallback for a hand-placed exit only: every exit the builder creates has the
field overwritten from the builder's value, so 10 is the number that ships.

**Ten, and it was six.** Six was chosen from the difficulty curve when a room
was one movement of eight chunks and 64-72 metres -- a run of two to three
minutes, which is a demo rather than a game. Rooms now run three movements and
191-211 metres each, and the ramp was stretched to match. Three things set the
count, none of them feel:

- **The ramp.** `Difficulty = Min(0.5 + index * 0.07, 1)` opens at 0.50, reaches
  0.99 at room 7 and clamps from room 8. At the old slope it hit full difficulty
  at room 3, which over ten rooms would have meant seven identical ones.
- **The ability gate.** `Allowed(kind, index)` downgrades a chunk the player
  cannot yet answer: `Chimney` below room 1, `DashGap` below 2, `Arena` below 3,
  `WallShaft` below 4. A six-room run would leave two rooms in which the full
  movement kit is ever asked for.
- **The boss.** `IsBossRoom => RoomIndex == RunLength - 1`, so the last room is
  the Warden and the count has to leave a run in front of it.

**Layouts are dealt from a bag, and the second half has its own set.**
`index % 3` was periodic by construction: over ten rooms it dealt layout 0 four
times and guaranteed room 6 was room 0 with a steeper ramp, which is the shape
of "the second half is the first half with bigger numbers". Now each half deals
every layout once before any repeat, ordered so no two neighbouring rooms share
one, and the back half draws from three LATE layouts built on the composition
kinds -- one climbs, one runs, one fights.

The last room is the Warden's and has a shape rather than a number: short, flat
and wide. Under the late set it drew the climb, and a 35-metre ascent
immediately before the only boss in a twelve-minute run is a stairwell with a
fight at the top.

Capping the run risked hollowing out the tests that walk many rooms, which is
the vacuous-pass class this project has already been bitten by three times.
Checked rather than assumed: `RebuildAs` is the mechanism and `RoomExitTrigger`
is the policy, and every other room test (`LongSession`'s 20 cycles,
`RoomChurn`, `PickupSkip`, `WallJumpReachable`) calls `RebuildAs` directly, so
none of them route through the cap. `Progression` does cross a real exit, and
asserts `RoomIndex == 1`, well inside it.

The run-ending check plays the whole run by teleporting onto each exit -- walking it
with real input would make the check a platforming benchmark whose failures
would be about jump tuning, not about the arc. It runs two steps past the end
on purpose: if the ending fails to stop the cycle, the surplus steps push
`RoomIndex` past `RunLength`, which the assertion catches. Verified by
restoring the endless cycle, which drove `RoomIndex` to 8 and turned the check
red.

## The second enemy type, and the hook that was missing

`CrossbowSentry` exists to make the movement kit matter. One melee grunt means
every encounter has the same answer -- walk up, trade hits -- and dash, double
jump and wall jump are decoration during a fight. A stationary enemy that
fires from nine units has to be closed on through fire, dashed past, or
jumped over.

**It does not move, and that was a limit before it was a design choice.**
`EnemyController.TickChase` and `TickPatrol` are private, so when the sentry
was written there was no hook for backing away to hold range. Rather than
reimplement `_PhysicsProcess` in the subclass to fake one, it was built out of
the hooks that existed: `PatrolSpeed` and `ChaseSpeed` at zero, a long
`AttackRange`, and an override of `ApplyAttackDamage`. A stationary sentry is
an honest enemy; a "kiting archer" that cannot kite would be a lie in the type
name. The hook has since been added -- see below -- and the sentry stays
stationary because that is now what it is for, not because nothing else was
possible.

**The bolt is parented to the room, not to the firer.** A projectile already in
flight should not vanish because the enemy that fired it died. It carries its
own lifetime for the same reason a fall-death plane exists: an entity that
leaves the level and is never cleaned up is a leak that only shows after a long
session.

**Two things the capture caught that the check could not.** The first sentry
was the same KayKit knight as the grunt, distinguished by a dagger instead of a
sword and a missing shield -- invisible at the camera's distance. An enemy you
cannot identify before it acts cannot be planned around, so the sentry is
tinted steel-blue. The first tint was an *unshaded* overlay, which identified
the enemy but pasted a flat silhouette over it and threw away the torch
lighting that says where it is standing; the tint is now lit.

**The leak assertion in the ranged-enemy check was vacuous on its first attempt**, and only
mutation testing showed it. A bolt that hits despawns on impact, so a test that
watches a bolt fired *at* the player never exercises the timeout at all:
deleting `Lifetime` entirely left the check green. It now also fires a bolt
that misses, and asserts that bolt was still airborne mid-flight before
asserting it is gone -- otherwise the cleanup claim would pass on a bolt that
never existed, which is the same vacuous-pass class this project has hit four
times now.

## The steering hook, and why it returns a place rather than a speed

The obvious extension point would have been "let a subclass choose its chase
speed". It does not work. Steering runs through `MoveToward`, which always
travels **toward** `_steerTarget`, so a speed-only hook can close or stand
still and nothing else -- a negative speed would move the enemy backwards along
whatever axis the target happened to be on, not away from the player.

`ChaseSteering` therefore returns `(Target, Speed)`. A type can close (the
player's position, the default), hold a range (an offset from the player),
retreat, or freeze (its own position). Two supporting pieces came with it:
`AttackReady`, so an override can tell whether this is the moment to commit,
and `FloorAhead`, so it can refuse to reverse off a ledge -- patrol already
refuses, and an enemy that backs into a pit reads as broken rather than as
retreating.

**The hook shipped with its consumer in the same change.** `Skirmisher` closes
the moment its attack is off cooldown and backs out of reach while it recovers.
This project already carries one unused parallel system and has documented what
that cost twice; adding a second extension point with nothing behind it would
have been the same mistake with better intentions.

**The first retreat implementation was a treadmill.** Offsetting the retreat
target from the enemy's *own* position recomputes the goal every frame, so the
goal keeps moving ahead of it -- measured at 6.90 units of flight for a
`RetreatDistance` of 4, running until it lost sight of the player entirely.
Anchored to the *player's* position it settles at 3.68 against a nominal 4 and
holds there, staying inside `LoseSightRadius` so it re-engages. `SteerDeadzone`
exists for that hold: the old hard-coded 0.05 made a range-holder stutter
left-right every frame around a point the player keeps moving.

**How the retreat is measured.** Not as the distance between the two, which a
knockback inflates whether or not the enemy moved. The enemy-behaviour check pins the player's X
at the instant the attack lands and measures how far the enemy travels away
from that fixed point, with a `BasicMelee` running the identical measurement as
a control: 2.38 units against 0.00. The control is the part that matters -- "the
skirmisher moved" is not the claim; "it moves differently from the default" is.

**Placement was wrong twice, and only an assertion caught it.** The type cycle
first counted encounter points, which the skipped spawn gauntlet shifted, so
the first sentry landed on the fourth encounter. Counting enemies placed fixed
that but restarted the cycle each room, and a generated room holds about two
enemies -- so with three types the third appeared nowhere, measured as zero
skirmishers in room 1. The cycle now carries across rooms as
`(RoomIndex + placed) % 3`, which also makes consecutive rooms open with
different enemies. Neither error was visible in the code; both were visible the
moment a check counted what a room actually contained.

## Three types alone is not three types together

Each enemy has its own check and each passes in isolation. That is a weaker
claim than it sounds, because the thing most likely to be wrong is not any one
type -- it is the cycle that distributes them, which is a property of the run.
That cycle was wrong twice and neither error was visible in the code:

| Attempt | What it counted | What went wrong |
|---|---|---|
| 1 | encounter points | the skipped spawn gauntlet shifted it; the first sentry landed on the fourth encounter |
| 2 | enemies placed, restarting per room | a room holds about two enemies, so with three types the third appeared nowhere -- zero skirmishers in room 1 |
| 3 | enemies placed, carried across rooms | correct, and consecutive rooms now open with different enemies |

Measured over a full six-room run: 2, 4, 1, 2, 4 enemies per room, no empty
room, all three types present.

The enemy-mix check also tears a room down **with a bolt in the air**. That is the
integration risk the per-type checks structurally cannot reach: `RebuildAs`
frees every child of the room, and a bolt is a child of the room precisely so
it outlives the enemy that fired it. Its first version was vacuous in the way
this project keeps rediscovering -- teleporting from exit to exit meant the
player was never in a sentry's view for the 0.55s windup, so it reported `peak
bolts in flight: 0` and its cleanup assertion proved that zero bolts had been
cleaned up. It now parks the player where a sentry can see it and asserts a
bolt was airborne before asserting the teardown left none.

## Measuring threat, and getting the attribution wrong first

Adding two enemy types changed the danger of every room after the first, and
nothing measured that. The threat check stands a player in each room for four seconds
without letting it hit back.

**The first version measured the wrong thing and looked fine.** It totalled all
damage taken and called it threat. Mutation testing was supposed to catch that
-- setting `AttackDamage` to zero should have driven it to zero -- and the
check stayed green at 56 damage, which read exactly like spike traps being
counted as enemies.

**That diagnosis was also wrong**, and only a control run settled it.
`CrossbowSentry` and `Skirmisher` assign their own `AttackDamage` in `_Ready`,
so zeroing the base class default disarmed the melee grunt alone; the surviving
56 was the other two types, working correctly. Two plausible stories, one
measurement: run the same exposure twice at the same coordinates with
`SpawnEnemies` on and then off. Hazards contributed **0**, so all 110 was the
roster. Re-run with all three types disarmed, the attributable figure is 0 and
the check fails.

The lesson is not "test harder". It is that a mutation which does not move a
number tells you *something* is wrong with the pairing, and the tempting
explanation is not evidence -- a control run costs one extra pass and answers
which of the two stories is true.

**The bound is two-sided.** Enemies that cannot hurt a stationary player are
decoration; an opening room that kills one in four seconds is not an opening
room. It also closes a gap the per-type checks left: the enemy-damage check proves *an* enemy
can damage the player, and a sentry or skirmisher tuned to zero would have gone
unnoticed by everything else.

Measured: 110 damage over four rooms of four-second exposure, one death, and
30 of 100 health in the opening room.

## Playing the game, and six wrong diagnoses on the way

Every check in this suite teleports the player. That keeps them honest about
what they test and leaves them silent about the one claim a platformer rests
on: the Spatial Metric Contract says every chunk is sized for the player's
abilities, and until the traversal check nothing had pressed a button to find out.

The bot is deliberately clumsy -- hold right, jump at a ledge or a wall,
double-jump near the apex, dash the gaps too wide to jump, swing at whatever is
in the way. A room a bad bot can cross is one the contract really does
guarantee. It reports distance as well as pass/fail, because a bot that stops
needs a human to say whether the level or the bot is at fault.

Its first run stopped at 84% of room 0, and the six diagnoses that followed
were all wrong. Each was a plausible reading of a real measurement:

| Reading | Why it was wrong |
|---|---|
| "nothing is blocking it" | the wall probe was cast at the *enemies'* capsule offset -- over the player's head, since the player's capsule is centred on its origin |
| "an enemy is blocking it, 0.16 away" | that distance was measured on X alone; the enemy was on a ledge overhead |
| "then teach the bot to attack" | 32 attacks later it was still stuck, so the blocker was not what was being hit |
| "an enemy pins it" | the trajectory showed it pinned with the nearest enemy 26 units away |
| "something damages it every cycle" | the damage signal fired twice in the whole run, neither during the stall |
| "it is walled in" | a five-height probe map showed clear ground -- but it was cast from the knocked-back position, not from the wall |

What settled it was asking the engine directly instead of inferring: the slide
collisions, then the damage signal's own payload, then the movement signals.
The last of those named the cause in one line -- the player *jumped* and left
the ground moving backwards, because it was standing against the chimney wall.

**The finding was the bot, and it was worth having.** Room 0 grants exactly
Dash and DoubleJump, and its chimney needs DoubleJump. The bot held the ability
and never used it, because it only ever pressed jump while grounded. Adding an
air jump took it from 84% to 98%, and switching the success condition from a
coordinate comparison to the real `RoomExit` trigger -- 66.9 against 67.9 is a
rounding margin, not a traversal result -- took it to the end.

### The press edge

Extending the bot to the other two layouts stalled it again: 14% of the wall
shaft, and 32% of the chimney layout. The tempting reading was that a generated
shaft is not climbable, which would have been a serious level-design defect. It
was not true, and settling it needed the mechanic taken out of the level
entirely -- the wall-shaft climb check builds two walls at exactly the dimensions `MicroChunk`
computes and drives the controller's own stated preconditions.

That probe found the real cause, and it was not in the game at all. The bot
drove press and release from separate modulo timers, so most presses landed on
a button that was already held. `PlayerController` fills its jump buffer from
`IsActionJustPressed`, and an already-held button produces no edge:

| | Presses | Wall jumps the game performed |
|---|---|---|
| Bot in the shaft, separate timers | 33 | 8 (and 42 double jumps, each burning the air jump) |
| Isolated probe, same fault | many | **0** -- 600 frames of wall *sliding*, `vy` pinned at the -3.0 slide clamp |
| Isolated probe, one release-then-press gate | 9 | **9** -- climbed 6.59 units of a 5.60 shaft |

Routing every press through a single release-then-press gate took the bot from
one layout to all three: 612, 578 and 1456 frames, identical to the frame
across three runs. One refactor of that gate briefly put the release on an
`else` branch, which held the button down forever -- 12 presses in 1601 frames,
none answered -- and the same refactor dropped the stall nudge, without which
the bot has no reason to jump at the foot of a chimney (ledges, so no wall
ahead, and floor ahead is true).

Verified by withholding the WallJump grant: room 0 still crosses, because it
does not need it, and rooms 1 and 2 both fail.

**The wall-shaft climb check stays separate from the traversal check on purpose.** A bot that cannot climb
proves nothing about whether a shaft is climbable, and the two questions fail
independently: one is about level dimensions, the other about the traversal
harness.

Verified the only way that means anything: with `RequireAbility` granting
nothing, the same bot reaches 80% and the check fails.

The general lesson is the one this project keeps relearning at a different
altitude. An indirect probe answers the question you encoded, not the question
you asked, and a plausible story about a real number is still not evidence.
When something can be asked directly -- a collision list, a signal payload --
ask it.

## The viewport's own background is content

Two defects sat in every frame of the game and neither was in any scene file.
Both were found by photographing the run-complete banner -- a capture taken to
check one thing, which showed something else.

**The backdrop began at the origin.** `ParallaxBackdrop` tiled its layers from
`x = 0` upward, while the orthographic camera, framing a player who spawns at
`x = 2`, sees back to roughly `x = -7`. Every room therefore opened with a bare
band down the left of the screen. `LeadX` now tiles 24 units behind the origin,
which covers the camera's half-width (~9.3) plus the parallax lag -- the near
layer moves at only 0.35 of the camera's travel, so it falls behind, and the
lead has to cover that too.

**Nothing was painting the background.** `Main.tscn` has no `WorldEnvironment`,
so the viewport cleared to Godot's default mid grey. The parallax layers sit at
`y = 4.0` and `y = 7.4` and cannot cover the bottom of the frame, and no
backdrop covers every camera position, so the fix belongs in the clear colour
rather than in more geometry:
`environment/defaults/default_clear_color` is now a near-black blue.

Measured on the same capture, sampling 81 rows per column for flat mid grey:

| Column | Before | After |
|---|---|---|
| x = 0 | 60 / 81 | 0 / 81 |
| x = 576 (mid-screen) | 0 / 81 | 0 / 81 |

The mid-screen column is the control: it was already correct, so a "fix" that
moved it would have been repainting the game rather than filling a gap.

Neither defect is caught by a gate check, and deliberately so. A pixel
threshold on one capture would encode the current art, not a property, and this
project already had three checks whose thresholds were derived from the value
under test. The honest coverage here is that captures get looked at.

## Every ability must be reachable, and every declaration must be real

A recurring class of defect here was not broken code but *content that did not
exist behind a promise*. Nothing failed a test, because there was nothing
running to fail. Found and closed:

- **WallJump** — implemented in `PlayerController`, present in `AbilityFlags`,
  shown in the HUD, and: granted by nothing, and no surface in the game had
  collision to slide on. The backdrop walls are scenery. Fixed by the
  `WallShaft` chunk, which builds collidable walls and grants the ability.
- **ChargeAttack** — an `AbilityFlags` value with a HUD icon and no mechanic at
  all. Now a real hold-to-charge heavy attack (2.2x damage, 1.8x knockback),
  granted before the `Arena` chunk.
- **`PhysicsLayers.Hazard`** — a layer constant with nothing ever on it, i.e.
  the whole category of environmental danger existed only in an enum. Now
  carries `SpikeHazard`.
- **`PhysicsLayers.Pickup`** — declared, while `AbilityPickup` sat on layer 0.
- **`SaveData.WorldFlags`** — declared, never written, never read. Removed
  rather than left as a promise of persistence nothing provides.

The question that found all five is not "does it work?" but **"is everything
implemented also reachable?"** Worth asking again whenever a flag, a layer or
a state is added.
