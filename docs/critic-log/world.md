## Round 1 — 2026-09-04

**Level/gating design: 7/10**

Verified against the actual `.tscn` files, not the build report's claim.
`scenes/world/Room1.tscn`, `Room2.tscn`, `Room3.tscn` exist; Room1 has a
`DashGate` (`AbilityGateBlocker`, `RequiredAbility = 2`) and Room3 has a
`DoubleJumpGate` (`RequiredAbility = 1`) — cross-checked against
`AbilityFlags.cs` (`DoubleJump = 1<<0 = 1`, `Dash = 1<<1 = 2`), so both
claimed gates are real and correctly typed, not just present. `WorldGraph.
BuildDefaultLostCrownlikeGraph()` wires room1↔room2 as `Dash`-gated both
directions and room2↔room3 as ungated, matching Room1's DashGate on the
room1→room2 side. `AbilityGateBlocker.Refresh` correctly does a bitwise
AND-equality check (`(unlocked & required) == required`) and drives both
`Visible` and `CollisionShape3D.Disabled` together — the StaticBody3D
approach (not Area3D) is real collision, verified by reading the shape
setup in each room's `.tscn`, not just the doc comment. `LevelManager`
subscribes to and correctly handles all three EventBus contract events
(`AbilityUnlocked`→`RefreshAbilityGates`, `LevelTransitionRequested`→
`LoadLevel`, `CheckpointReached`→ records id), confirmed by grepping the
emit sites (`LevelTransitionTrigger.cs:31`, `CheckpointTrigger.cs:31`) back
to matching subscriptions.

Two real issues stop this short of 8+:
1. Room3's DoubleJumpGate sits *inside* the room (blocking `VaultLedge`,
   past the free entry point), while `WorldGraph`'s room2→room3 connection
   is declared `RequiredAbility = None`. Defensible (the graph models
   room-reachability, not every internal obstacle) but nothing in
   `WorldGraph.cs`'s doc comment says gates can live off-graph like this,
   so a designer reading the graph alone would wrongly conclude room3 is
   fully open once reached.
2. `LevelManager._checkpointPositions` (line 31) is written on every
   `CheckpointReached` (line 156) but never read anywhere — grepped the
   whole `src/` tree, zero consumers. There's no respawn-on-death path
   using it; `CurrentCheckpointId` is likewise write-only within this
   file. Either respawn logic is missing entirely or lives in a subsystem
   that was never wired to this data — as it stands this is dead state.

**Visual fidelity: N/A (not a meaningful score for this subsystem)**

Ran the one prescribed capture directly on `Room1.tscn` (windowed, 30
frames, shots at 15/30). Both PNGs are flat, uniform mid-grey
(`docs/critic-captures/world/frame_0015.png`, `frame_0030.png`),
byte-identical in composition — no camera, no light, no visible geometry.
Expected: `Room1.tscn` has no `Camera3D`/`WorldEnvironment`/light of its
own (confirmed by its node list — only `Floor`, spawn points, trigger
Area3Ds, and the DashGate), since World's job is collision blockout data
consumed by a level container, not a standalone viewable scene. This is
not a defect to score — there's nothing here for World to be visually
responsible for; a real shot would require PlayerCamera's rig loaded on
top of this room.

**Top issue:** `_checkpointPositions` / `CurrentCheckpointId` are recorded
but have no consumer anywhere in `src/` — checkpoints currently do nothing
beyond bookkeeping; respawn-at-checkpoint isn't actually implemented by
this subsystem or wired to whatever should implement it.

## Round 6 — 2026-09-06

**Level Design & Visual Architecture: 9/10 (was 7/10)**

Major architectural overhaul addressing player feedback regarding lack of landmarks, invisible exits/checkpoints, platform compenetration, and empty corridors.

**1. Level Exit Landmark (`RoomExitTrigger.cs`)**
- Visual representation: Authored stone archway (`wall_doorway.gltf` with wooden door removed) flanked by stone pillars (`column.gltf`), swaying banners (`banner_red.gltf`) and wall torches.
- Dimensional portal: MeshInstance3D with pulsing emissive material (`EmissionEnergyMultiplier = 2.8`), pulsing at 3.2 Hz. Ethereal cyan in transitional rooms, triumphant gold in room 9.
- Lighting beacon: `OmniLight3D` casting 10m guiding light down the corridor.
- Boss seal: Ruby energy barrier with 8.5m red light locking the arch until the Warden is defeated.

**2. Checkpoint Shrines (`CheckpointTrigger.cs`)**
- Sculpted stone plinth with floating resonant crystal rotating on Y and bobbing on Y with harmonic sine motion.
- Dual-state illumination: Dormant state glows softly in cyan (`LightEnergy = 0.9`); active state bursts with radiant gold/cyan aura (`LightEnergy = 2.4`, `Emission = 3.4`).

**3. Structural Integrity & Set-Dressing (`DungeonRoomBuilder.cs`)**
- Platform support columns (`BuildPlatformColumns`): GPU-batched stone pillars anchored from elevated walkways ($Y \ge 1.8\text{m}$, width $> 4.0\text{m}$) down to the floor.
- Environmental set dressing (`DecorateDungeon`): Themed clusters of crates, barrels, chests, and wall banners against the background wall ($Z = -1.5\text{m}$), zero impact on physics or gameplay.
- 3-Act aesthetic progression: Distinct lighting and palette across Act I (Forgotten Crypts), Act II (Sunken Catacombs), and Act III (Warden's Sanctum).

**4. Platform Clearance & Backdrop Integrity**
- Eliminated fake wooden doors from the backdrop wall (replaced with `wall_arched.gltf`).
- Eliminated horizontal compenetration on staircases (`Chimney`): Each step advances cleanly by 4.0m on the grid with zero horizontal overlap.
- Eliminated block protrusion: Replaced `floor_foundation_allsides` with 180° rotated `floor_tile_large` at $Y = -0.16\text{m}$.

**Verification:**
- `scenes/tests/ChunkClearance.tscn`: 8 of 8 chunks PASS at difficulty 1.0.
- `scenes/tests/FloorLegibility.tscn`: 417 platforms verified, 0 unsupported, 63 holes verified, 0 unlit.
- `scenes/tests/FullRunBot.tscn`: 10 of 10 rooms traversable with real physics input.

