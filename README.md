# LostCrownlike

A 2.5D action-platformer in the spirit of *Prince of Persia: The Lost Crown*.
Godot 4.7.2 (.NET / C#, net8.0), side-on orthographic camera, movement on the
X/Y plane with Z locked.

## Running it

Godot is vendored in `tools/` — nothing to install beyond the .NET 8 SDK.

```bash
tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64.exe --path game
```

`game/scenes/Main.tscn` is the game: a generated dungeon room, the player with
combat, patrolling enemies, ability pickups, checkpoints, and an exit that
leads to the next room.

**Controls** — A/D or arrows to move, Space to jump (twice once Double Jump is
picked up), Shift or K to dash, J or left mouse to attack, L or right mouse for
a heavy attack, Escape to pause.

Always `dotnet build` from `game/` before running: Godot loads the compiled
assembly, so C# edits are invisible until you build.

**Picking this up after a break?** Read [STATUS.md](STATUS.md) first: what the
game is now, every measured number, and the three decisions waiting on you.

## Repository layout

| Path | What it is |
|---|---|
| `game/` | The Godot project |
| `game/src/` | C# source, one directory per subsystem |
| `game/assets/kaykit/` | Art assets (CC0, see Licensing) |
| `STATUS.md` | Current state, measurements, and the open decisions |
| `ARCHITECTURE.md` | **The contract.** Subsystem boundaries, event vocabulary, shared types, and every deviation with the measurement that justified it |
| `PROMPT.md` | The original brief the project was built against |
| `docs/critic-log/` | Review rounds, with scores and the evidence behind them |
| `tools/` | Vendored Godot |
| `assets_src/` | Unmodified asset-pack downloads, kept so imports can be redone |

Read `ARCHITECTURE.md` before changing anything that crosses a subsystem
boundary. It is where the non-obvious decisions live, and most of them exist
because something measurably broke.

## Verification

One command runs the whole gate and exits non-zero if anything fails:

```bash
bash tools/verify.sh
```

Two rules learned the hard way, worth stating with the others. **An
edit that silently changes nothing is worse than one that fails.** A one-line
call was dropped by a search-and-replace whose pattern no longer matched, and
because nothing asserted the match, the build stayed clean, the code looked
right and four checks HUNG instead of failing -- a hang reads as "slow" long
before it reads as "broken". Every scripted edit in this project now asserts its
anchor before writing.

A sixth: **while the gate is running, change nothing it reads from disk.** The
obvious half is not rebuilding -- every check after step 1 runs the compiled
assembly, so a rebuild mid-run swaps it underneath and the rest of the gate
reports on a build that never passed step 1. The half that actually bit was
scenes: `.tscn` files are loaded fresh by every check, so editing one to point
at a class the running assembly does not have yet produced four engine errors
in checks that had nothing to do with the edit. Both times the result looked
like a finding and was an artefact.

Sixty-one checks, in order:

1. the build compiles
2. the main scene boots without errors
3. the save round-trip persists to disk and restores the player
4. all three scripted attacks connect
5. the full loop: checkpoint → death → respawn at the checkpoint that was current
6. abilities start locked, a pickup grants one, player and save agree; the
   check resets the save itself rather than relying on running fourth
7. no enemy walks off a platform into a generated gap
8. an enemy can actually damage the player — combat is not one-sided
9. an ability earned in a previous session is still there after a restart
10. dying before any checkpoint does not softlock the run
11. a corrupt save file does not stop the game from booting
12. pause actually stops the world, and it resumes afterwards
13. abilities, health and the save survive a room rebuild
14. repeated room rebuilds do not accumulate nodes
15. WallJump is granted somewhere and has walls to be used on
16. holding heavy deals more damage than tapping it — ChargeAttack is a mechanic
17. a spike trap is generated and damages on a repeating cooldown
18. the save is still loadable and matches memory after 20 rooms and 20 deaths
19. 1500 frames of random input mashing produce no degenerate state, and reach
    8 of the 9 movement states
20. wall slide — the ninth state, which mashing cannot reach — enters, clamps
    the fall for as long as it lasts, and ENDS when the wall does
21. the combo window escalates damage when chained and resets when it lapses
22. no two of the nine procedural sounds are perceptually identical, measured
    by length, spectral centroid and noisiness
23. falling out of the level kills the player and the run resumes
24. coyote time — a jump pressed just after leaving a ledge still fires
25. dash invulnerability — a hit landed mid-dash is ignored, the same hit later is not
26. the camera follows the player and keeps it framed
27. leaving and re-entering the same checkpoint announces it once, not every time
28. an enemy that falls out of the level is removed, not left falling forever
29. a rebuilt room does not re-offer an ability the player already owns, and
    no room offers the same ability twice
30. nothing leaves the X/Y movement plane under movement, jumps, dashes or attacks
31. the run has an ending: ten rooms, one completion, and the save records it
32. ranged enemies are placed, hurt at range, and leave no bolts behind
33. one enemy type retreats after striking, one holds a stand-off, the default closes
34. across a whole run every room is populated, every enemy type appears, a
    room torn down under a live bolt leaves none behind, and the second half
    of the run holds more enemies than the first
35. enemies account for real damage to a player who never hits back, and the
    opening room does not kill one who stands still
36. a reactive bot plays all ten rooms of a full run end to end with real
    button presses and reaches each exit trigger
37. a wall shaft of the size the generator builds can actually be climbed
38. five of the chunk kinds are each clearable alone at full difficulty
39. clips that describe a continuing state loop, on the player and on enemies
40. a finished run can be restarted, and the save stops claiming it is finished
41. every checkpoint in the hardest room stands on ground
42. the model is still animating three seconds into a run, not holding a pose
43. an incoming attack telegraphs: the arm draws back and the weapon lights up
44. a guard stops a blow, and only a perfect one staggers the attacker
45. corpses linger long enough to read, then sink and are freed
46. spaced sounds never cut a still-playing voice off; a real burst still steals
47. 4600 frames of real play WITH A RENDERER print no engine error
48. the ambience bed runs continuously on its own voice, without starving the pool
49. the pause menu is in the game, resumes cleanly, and restarts the run
50. the game boots into a title, with no world running behind it, and Play
    starts the run; no key drives two actions at once
51. the last room is a boss: armoured, a heavy blow is chip damage and does
    not interrupt; parried open, the same blow lands in full; and the run
    cannot end while it lives
52. a landed blow, a blow absorbed by armour and a perfect parry each leave a
    different mark, the camera is punched, and none of it accumulates
53. the options screen opens from the title, a key already taken by another
    action is refused, and a rebind is still there after a restart
54. an enemy turns to face the player mid-attack, and a stationary one turns
    at all
55. every wide platform has a stone face under it and every hole in the floor
    has a light in it
56. an arena cannot be run past: the barrier holds until the arena is cleared
57. enemies patrol the ground they were placed on, and the room still has its
    population ten seconds later
58. the game exports to a Windows binary whose .NET assemblies are present,
    and that binary starts with its C# autoloads running
59. the title offers Continue only when there is progress, it resumes the
    saved room, and a new run starts over with nothing
60. the music voice is never starved, its figure moves through more than a
    couple of pitches, and the boss room does not sound like the corridor
61. no check from 3 onward printed an engine error while reaching its own PASS

**One unresolved intermittent.** The engine-quiet check fired on two consecutive gate runs
immediately after a rebuild, naming engine errors, and has passed on the
sixteen runs since — including twenty solo runs of the five newest scene tests,
which produced none. It is recorded here rather than quietly rerun until green:
an intermittent that cannot be reproduced on demand is not a fixed one. The
gate now appends the offending output to `tools/.engine-errors.log` whenever
the check fires, so the next occurrence is diagnosable instead of being
another thing to chase afterwards.

The engine-quiet check is cross-cutting rather than a scene of its own. It exists because a
test asserting its own outcome says nothing about what the engine printed on
the way there: for a long time three tests were green while the shipped game
printed `Resource file not found: res://generated:1` on *every* room
transition. `RoomExitTrigger` announces a generated room on the transition
signal so audio, UI and save still hear it, but a generated room is rebuilt in
place and has no `.tscn`, and `LevelManager` was handing that string to
`ResourceLoader`. Nothing read stderr, so nothing noticed. Errors are pooled
into one line instead of reddening each test, so one root cause reads as one
failure, and the scan covers every check from 3 onward rather than only the
scene suite where this particular defect happened to land. The single exclusion is `N resources still in use at exit`, engine
shutdown accounting that appeared in about 1 run of 5 and never under
`--verbose`; everything else counts.

Checks 6 onward run their own scenes under `game/scenes/tests/`, each instancing the
real `Main.tscn`, so they exercise the assembled game rather than an isolated
subsystem. A build failure aborts the run rather than continuing — the later
steps execute the compiled assembly, so carrying on would test the *previous*
build and report passes that mean nothing.

The gate is verified in both directions: with deliberately broken source it
exits 1 and prints no PASS lines; with clean source it exits 0.

Individually, if you want just one:

```bash
cd game && dotnet build                     # must be 0 errors, 0 warnings
```

Headless checks (no rendering needed):

| Scene | Verifies |
|---|---|
| `scenes/RespawnTestScene.tscn` | Save round-trip to disk, and respawn at the recorded position |
| `scenes/AsyncLoadTest.tscn` | Threaded room load completes, and is genuinely deferred rather than synchronous |

Run one with:

```bash
tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe --headless --path game scenes/RespawnTestScene.tscn
```

The remaining drivers (`TraversalTest`, `RoomTransitionTest`,
`RebuildHitchTest`) are diagnostic rather than pass/fail and are **not**
attached to any shipped scene — add the script as a child node of `Main.tscn`
when you want one, and remove it afterwards.

### Writing a new scene test

Two things will bite you, both learned the hard way here:

**Locate nodes by walking the tree**, not via `GetTree().CurrentScene`. That is
what lets the same script work whether it is hand-attached to `Main` or living
in a test scene that instances `Main`.

**Frames are not seconds.** A headless run is uncapped, so `delta` differs on
every run and 60 frames can be a fraction of a second. Anything that waits on
time — a charge threshold, a `Timer`, an attack window — must be driven by
accumulated `delta`, not by a frame count. Three separate tests in this project
were written against frame counts and silently measured nothing: a charge that
only reached 0.17s of a 0.45s threshold, a UI timer that never fired, and a
fuzz that gave 8, 6 and 7 states on three consecutive runs *with the RNG seeded*.
Pass `--fixed-fps 60` when a test needs a reproducible timeline (the gate does
this for the fuzz), and seed any randomness.

A gate that fails at random teaches you to ignore it, which is worse than
having no gate. The five time-sensitive checks here were each run three times
to confirm they are stable before being trusted.

**Never derive a test's own timing or thresholds from the value under test.**
Three tests here did, and all three passed on a broken mechanic: the wall-slide
check asserted `fall >= -WallSlideSpeed`, so raising that tunable to 999 made it
trivially true; the dash check fired its hit at `DashIFrameDuration * 0.5`, so
zeroing the window meant the hit was never thrown at all. Use fixed absolute
bounds that state what the mechanic is *for*.

**Seed the right generator.** The audio synthesiser uses a .NET `Random`, not
Godot's — so `GD.Seed()` does nothing to it. A sound-separation check was
seeded, looked deterministic, and still failed roughly one run in six until the
synthesiser's own RNG was pinned. Anything measured off generated content needs
its actual source of randomness fixed, and the only proof is running it five
times.

**Check that a new test can fail.** Break the mechanic on purpose and confirm
the check goes red, then restore. Doing this across the suite here found that
disabling the fall-death plane entirely left every check green — the loop test
kills the player with direct damage and never falls — so a mechanic added to
fix a softlock had no coverage at all. The fall-death check exists because of that.
Three checks were also passing vacuously: "no enemy fell into a gap" was
trivially true with zero enemies, "node count stayed stable" was true when the
rebuilds never ran, and "the save matches memory" was true when nothing had
been written. Each now asserts its premise first.

## Visual capture

`scenes/CaptureRunner.tscn` drives any scene with scripted input and writes PNG
frames, per-frame timings, and an event log.

```bash
tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe \
  --path game scenes/CaptureRunner.tscn -- \
  --target=scenes/Main.tscn --out=docs/critic-captures/run --frames=300 --shot-every=60 \
  --script=5:move_right:press,40:jump,120:dash
```

**Run it windowed, never `--headless`.** Godot's `headless` display driver
supports only the `dummy` renderer, so `get_viewport().get_texture()` returns
null and the capture throws `Parameter "t" is null`. This is engine behaviour,
confirmed in Godot's own `--help`; `--rendering-driver opengl3` does not change
it. Plain `--headless` is still correct for boot checks and non-visual tests.

Two things to know when reading a capture:
- `frametimes.csv` — use the `wall_frame_ms` column. The `process_time_ms` and
  `physics_time_ms` columns come from `Performance.GetMonitor`, which only
  refreshes with a profiler attached and otherwise returns near-static values.
- The harness pins `Engine.MaxFps = 60`, so a p50 of 16.66ms is the cap, not
  the engine's headroom. Frame-time spikes land on screenshot frames — that is
  capture cost, not gameplay cost.

## Licensing

Art is **KayKit** by Kay Lousberg (`kaylousberg.itch.io`), released under
[CC0](http://creativecommons.org/publicdomain/zero/1.0/): free for personal and
commercial use, attribution not required. Credit is given here anyway.

- KayKit Character Pack: Adventurers 2.0 — player and enemy characters, weapons
- KayKit Dungeon Pack 1.1 — walls, floors, props, torches

Full licence texts ship alongside the assets in `game/assets/kaykit/`.
"# PrincessWarrior" 
