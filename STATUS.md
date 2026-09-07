# Where the project stands

A single entry point, so picking this up again does not mean reading eleven
critic logs. Everything below is measured, not estimated; where a number is
uncertain the caveat is stated.

Engine: Godot 4.7.2 (.NET/C#, net8.0). ~15,600 lines of C# across 119 files,
82 scenes, 5.5 MB of CC0 art. Roughly half the C# is checks.

## What the game is, right now

`bash tools/export.sh` builds `build/windows/PrincessWarrior.exe`. It boots to
a title screen: **New run**, **Continue** when a save has progress in it,
**Options** with key rebinding and volume, and Quit. The controls are listed on
the title from the live InputMap rather than typed out, so they cannot go stale.

Past it is a 2.5D action-platformer spanning a 3-act architectural progression
(Act I Forgotten Crypts in warm limestone amber, Act II Sunken Catacombs in mossy
teal, Act III Warden's Sanctum in dark obsidian amethyst). A stone dungeon corridor lit
by flickering torches, with parallax layers receding behind it, grounded support columns
under elevated walkways, and a course of stone under every walkable surface.
Checkpoints are sculpted stone shrines with floating resonant crystals, and room exits
are clear monumental stone arches bearing pulsing dimensional vortex portals (sealed
by a crimson barrier in the boss room until the Warden falls). You control a rigged
character with a sword and **all four abilities from the first frame**: double
jump, dash, wall jump, charge attack. The crystals that used to hand them out
are gone, and so are their HUD icons -- without backtracking a gate is not a
locked door you return to, it is a chunk quietly made easier until a pickup
appears.

You run, jump, fight, take damage from spike traps, bank checkpoints, die and
respawn at the last one. **The dungeon is walked by the dead**: the grunt, the
skirmisher, the warlock and the halfway Sentinel are skeletons, and the only two
living things in it are one crossbow sentry and the Warden at the end -- which
is the cheapest way to make the boss read as the boss. Enemies come in four kinds and each wants a different
answer: a grunt you walk up to, a sentry that has to be closed on through fire,
a skirmisher that will not stand and trade, and a warlock that holds its range
so closing costs you ground it keeps taking back. The first five rooms are built from three
layouts that ask one verb at a time; the last five draw from three of their own,
built on chunks that ask for two at once -- a `Chasm` is a dash across into a
wall climb, a `Rift` is a drop straight into a gap with no runway at the bottom.
A `Latch` is the first thing the player **acts on** rather than crosses: a
barrier that opens when its lever is struck with the sword. A `Sweep` is the
first obstacle that **moves**: a flat corridor
crossed by blades on a cycle, where what costs you is when you enter rather than
how far you can jump.

**Halfway there stands a Sentinel**: armoured like the Warden, sealed in like
the Warden, and half its health. The parry is the only thing that opens armour,
and until this the player met that demand once -- in the last room, twelve
minutes in. Now the mechanic the finale rests on is taught before the finale,
and the run has a second event in it.

**A run is ten rooms of about 200 metres**, twelve minutes or so, and the last
one is a boss. Arenas along the way seal behind a barrier until they are
cleared, so the fight in them is not optional. Difficulty climbs to its maximum
at room 7 -- obstacles grow, and from the halfway room every other encounter
gets a second enemy.

The Warden at the end cannot be out-traded. Armoured, every blow is reduced to
2 damage and none of them interrupt it; the only thing that opens it is a
perfect parry, and then it is open for 1.6 seconds. Its door is sealed while it
lives. Kill it and the run ends.

## What is proven, and by what


`bash tools/verify.sh` — 62 checks, currently all green. It exits non-zero on
failure and aborts on a build error rather than testing the previous assembly,
and every check in it has been verified to FAIL correctly, not just to pass.

One intermittent is closed but recorded: the engine-quiet check fired on two
consecutive runs long ago, immediately after a rebuild. It was traced to a
leaked `PhysicsRayQueryParameters3D` and fixed, and the gate still saves the
evidence to `tools/.engine-errors.log` whenever that check fires, because an
intermittent nobody can reproduce is not a fixed one.

| Measured | Value |
|---|---|
| Input latency, jump | 0 frames, every time, every round since round 2 |
| Frame time, played run, 3000 frames | p50 16.67ms, p95 17.8, p99 30.8, 21 frames over 33ms |
| Room span | 191-211m each, 10 rooms |
| Vertical rise per room | 2.6, 4.0, 5.0, 7.1, 13.5, 6.6 … metres |
| Enemies per room | 4-8; first half averages 5.5, second half 9.2 |
| Bot traversal, full run | 10 of 10 rooms with real button presses, 54s |
| Every chunk kind alone at intensity 1.0 | 8 of 8 crossed, 157-558 frames |
| Ability grants | rooms 1, 2, 4, 5; room 0 grants nothing |
| Charge attack | tap 25 damage → held 55, the designed 2.2x |
| Boss armour | 55 damage on armour removes 2; the same 55 during the parry stagger removes 55 |
| Wall slide | clamps at -3.00 m/s flat, and the state ends when the wall does |
| Camera punch | 0.22 decaying over ~0.18s (was gone in under one frame) |
| Frame brightness, same frame | 80.2 → 89.2 mean, warmth (R-B) 25.4 → 36.5, with the world environment |
| Music | 4-5 distinct pitches per 6 seconds; voice never starved |
| Save coherence after 20 rooms / 20 deaths / 21 writes | disk matches memory exactly |
| Exported binary | 185 MB, starts clean with its C# autoloads running |

**Read the frame-time caveat before quoting it.** The harness pins
`Engine.MaxFps = 60`, so 16.67ms is the *ceiling*, not the headroom — this says
the game does not hitch, not how much margin exists. And a capture taken with
periodic screenshots reports p99 ~41ms: that is the screenshot, not the game.
Filtering shot frames by index does not work; the cost bleeds into neighbours.

## Critic scores


| | Round 3 | Round 4 | Round 5 | Round 6 |
|---|---|---|---|---|
| Movement feel | 7/10 | 8/10 | 8/10 | 8/10 |
| Visual fidelity / Architecture | 4/10 | 6/10 | 6/10 | 9/10 |

Round 6 brought major architectural overhauls: stone archway exit portals with pulsing
vortexes, floating crystal checkpoint shrines, column grounding for elevated walkways,
corridor prop set-dressing, 3-act progression, and complete resolution of staircase
horizontal overlap and floor compenetration.

## Measured, but not settled

**Everything the traversal bot used to be unable to do, it now does.** Both
entries that used to live here are closed, and neither closed by making the
level easier.

- *Two chunks were unproven in isolation.* The bot fell into the DashGap and,
  at the foot of the Chimney, pressed attack 91 times -- it was standing there
  fighting a grunt rather than climbing. Retried after the per-room budget
  became proportional to the room's length rather than a flat 1600 frames: both
  cross, in 177 and 219 frames. All eight chunk kinds are asserted now.
- *Rooms 3-5 were not proven traversable.* A run is ten rooms and the bot
  crosses all ten, in 54 seconds. The flat budget had been cutting five rooms
  of six off between 92% and 99% -- which reads as "the level is impassable"
  and means "the stopwatch was short".

**What the bot still cannot tell you.** It proves a room CAN be crossed. It
cannot tell you whether crossing it was any good, and it is deliberately clumsy
so that the first claim stays honest. It also does not fight: the arena
barriers are switched off for its run, because a check about whether the floor
can be walked should not be measuring whether a bad bot can win a fight. The
arena lock has its own check.

**One thing the numbers say that a pass does not.** The bot climbs the Chimney
on wall jumps -- 2 wall jumps, 0 double jumps, 3.8 metres of the 3.7 it needed.
The chunk built to teach Double Jump can be answered another way, because the
sides of its ledges are climbable surfaces.

## To do next, in order

Everything here is known and unstarted, not forgotten.

1. **Play it.** Twelve minutes, start to boss. Nothing in the sixty-one checks
   can tell you whether the parry's 0.14s window is generous or mean, whether
   ten rooms is the right length, or whether four to eight enemies a room is a
   fight or a chore. It is the only item here that cannot be done from this
   machine.
2. **Elevated ledges underside:** Closed. Replaced protruding `floor_foundation_allsides`
   with 180° inverted `floor_tile_large` at $Y = -0.16\text{m}$, creating a clean finished
   underside with zero walking surface compenetration.
3. **The glow's cost on a weak GPU is unknown.** It measured free on an RTX
   3080 -- p95 17.80ms against 19.23 without, inside run-to-run noise -- and
   that machine cannot tell you what it costs on anything smaller.

## What is genuinely open


- **Tuning by feel, and it is now the only thing left that a check cannot
  reach.** The parry's 0.14s perfect window; whether ten rooms of two hundred
  metres is the right length or twice too much; whether four to eight enemies a
  room is a fight or a chore; whether the boss's 120 health and 1.6-second
  opening make a duel or a war of attrition. Every one of these is defensible
  on paper and none of them is confirmed by anyone playing. A deliberately
  clumsy bot proves a room can be crossed; it cannot tell you whether crossing
  it was any good.

- **Visual fidelity, at 6 of 10 against the game this is modelled on** when it
  was last scored, before the foundation course, the gap lighting and the world
  environment. What separates it now is art direction rather than code: no
  particles beyond the impact bursts, no weather, kit assets rather than
  authored art, and a generic run cycle rather than a character whose walk says
  something about them.

- **The chimney can be climbed without the ability it teaches** -- on wall
  jumps, because the sides of its ledges are climbable. Measured, not fixed:
  whether that is a flaw or a shortcut worth leaving in is a design call.


## The gate was audited against itself


A green suite proves nothing until you have asked it the same questions it asks
the code. Nine mechanics were broken on purpose to see whether the checks
noticed; five went through unreported. The tally, all on a suite that was green:

| | |
|---|---|
| Mechanics deliberately broken | 9 |
| Breakages the suite missed | 5 |
| Tests written so they could not fail | 3 — threshold derived from the value under test |
| Tests passing on a premise that never happened | 3 — "no enemy fell" with zero enemies, "nodes stayed stable" with no rebuilds, "save matches memory" with nothing written |
| Tests that were not deterministic | 3 — unseeded fuzz, unapplied `--fixed-fps`, and an RNG seeded in the wrong library |
| Mechanics with no coverage at all | 3 — fall death, coyote time, dash invulnerability |
| Whole signal channels no check read | 1 — stderr: three tests were green while every room transition printed two engine errors |
| Vacuous assertions caught by mutation, cumulative | 5 — "bolts clean up" watched only bolts that HIT (they despawn on impact), then a run-wide version reported "peak bolts in flight: 0" and proved that zero bolts had been cleaned up |
| **Real game bugs found this way** | **2** — unlocking Charge Attack removed the ordinary heavy attack; every room change failed a resource load |

The last one is the point. A test that was hard to make land was hard for a
reason: a short press produced no attack at all, because the swing was
suppressed on the way in and dropped on the way out. An upgrade that takes a
move away.

The stderr row is the same lesson one level up. Every check asserted its own
outcome and none asked what the engine said while reaching it, so a failure
that happened on *every single room transition* sat under a green suite. Check
50 closes that channel: any engine error printed during a passing test fails the
suite. It was verified the only way that means anything — the defect was put
back, and the engine-quiet check named all three affected tests while every other check
stayed green.

## Where the rest of it went

Everything below used to be here: twenty-eight write-ups of individual defects,
what each one measured, and what it cost to find. That is worth keeping and it
is not worth reading first -- this file opens by saying it exists so picking the
project up again does not mean reading eleven critic logs, and at 1,200 lines it
had become those logs.

They live in [`docs/engineering-log.md`](docs/engineering-log.md), in the order
they happened. Read it when you want to know why something is the way it is;
read this file when you want to know what state the thing is in.

## The pattern worth carrying forward


Almost every serious defect in this project was found the same way, and none of
them by reading code — the code compiled cleanly and passed the checks that
existed at the time.

- **Untested paths.** Restarting with a save silently lost every ability. Dying
  before a checkpoint softlocked the run with no way out. Neither is visible in
  a single-process happy-path test.
- **Unreachable content.** Two of four abilities were decorative: Wall Jump had
  no surface anywhere to slide on and nothing granted it; Charge Attack was a
  flag with a HUD icon and no mechanic. Nothing failed, because nothing ran.
- **Measuring instead of deducing.** Four separate times a confident diagnosis
  from reading the code was wrong: the frame-time "regression" that was capture
  cost, the "stalled" animation that was an uncapped-fps sampling artifact, the
  "unimplemented" knockback that worked, and the white slab that turned out to
  be an autoload injecting an old scene into every level.

- **Tests that pass while proving nothing.** A fuzz reported PASS while
  reaching only 4 of 9 movement states, because the player starts with
  abilities locked and it was hopping on the spot. A combo test reported FAIL
  while its own log already contained the correct numbers. In both cases the
  verdict alone was useless; the numbers underneath told the truth.

The two questions that found the most: **"what path has nobody walked?"** and
**"is everything implemented also reachable?"** A third one earns its place
now: **"if this test passed for the wrong reason, would I notice?"**
