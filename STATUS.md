# Where the project stands

A single entry point, so picking this up again does not mean reading eleven
critic logs. Everything below is measured, not estimated; where a number is
uncertain the caveat is stated.

Engine: Godot 4.7.2 (.NET/C#, net8.0). ~7,000 lines of C# across 68 files,
35 scenes, 2.9 MB of CC0 art.

## What the game is, right now

The game boots into a title screen — Play, Quit, and a controls list built
from the live InputMap rather than typed out. Past it is a playable 2.5D
action-platformer:

A stone dungeon corridor, lit by flickering torches with parallax layers
receding behind it. You control a rigged character with a dagger, starting
with **no abilities**. You run, jump, fight a patrolling knight, take damage
from spike traps, bank checkpoints, die and respawn at the last one. Ability
crystals grant Dash, Double Jump, Wall Jump and Charge Attack — each placed
immediately before the obstacle that needs it. A door at the end leads to the
next room, generated with a different layout and a higher difficulty.

A run is **six rooms**, and the last one is a boss. Its door is sealed while
the Warden lives, and the Warden cannot be out-traded: armoured, every blow is
reduced to chip damage and none of them interrupt it, so the only way it opens
is a perfect parry. Kill it and the run shows RUN COMPLETE instead of opening
onto another corridor. Difficulty climbs for the
first three rooms and is at maximum for the last three.

## What is proven, and by what

`bash tools/verify.sh` — 54 checks, currently all green, and green on the last
sixteen consecutive full runs. Two runs before those failed the engine-quiet check (
errors) immediately after a rebuild and have not reproduced since; the gate now
saves the evidence to `tools/.engine-errors.log` when that check fires, because
an intermittent nobody can reproduce is not a fixed one. It exits non-zero on failure and aborts on a build error
rather than testing the previous assembly. It has been verified to fail
correctly, not just to pass.

| Measured | Value |
|---|---|
| Input latency, jump | 0 frames, every time, every round since round 2 |
| Frame time (controlled: one screenshot at run end) | p50 16.67ms, p95 17.01, p99 17.25, max 22.49 |
| Frame time during a full PLAYED run, 3 runs | p50 16.67ms, p95 17.4-20.7, p99 23.3-26.4 |
| Room rebuild, phase-timed | 450ms -> 155ms, then spread over 8 frames |
| Frame time after spreading the rebuild, 3 runs | p95 17.1-17.4, p99 17.9-25.6, frames over 33ms: 13-23 (was 117) |
| Deaths in an opening run | 0 (was 2 before the difficulty ramp) |
| Movement states covered | 9 of 9 — eight by fuzzing, wall-slide by a targeted test |
| Node drift over 12 room rebuilds | +4 on a 424 baseline, explained by layout size |
| Save coherence after 20 rooms / 20 deaths / 21 writes | disk matches memory exactly |
| Charge attack | tap 25 damage → held 55, the designed 2.2x |
| Bare viewport at room start | 60 of 81 sampled rows flat grey → 0 |
| Sentry bolt | 8 damage at 6 units, gone 2.5s after a miss |
| Skirmisher retreat | backs off 2.38 units from the strike point; BasicMelee control 0.00 |
| Enemies over a six-room run | 2, 4, 1, 2, 4 per room; no empty room; all three types present |
| Damage to a passive player, 4s per room | 110 over 4 rooms, all of it attributable to enemies (hazards-only control: 0) |
| All three layouts played with real input | crossed in 612 / 578 / 1456 frames; identical to the frame across three runs |
| Wall shaft climb, full difficulty | 7 wall jumps lift 8.23 units of an 8.00 shaft — 1.18 per jump |
| Combo | chained 10 → 12, resets to 10 when the window lapses |

**Read the frame-time caveat before quoting it.** The harness pins
`Engine.MaxFps = 60`, so 16.67ms is the *ceiling*, not the headroom — this says
the game does not hitch, not how much margin exists. And a capture taken with
periodic screenshots reports p99 ~41ms: that is the screenshot, not the game.
Filtering shot frames by index does not work; the cost bleeds into neighbours.

## Critic scores

| | Round 3 | Round 4 | Round 5 |
|---|---|---|---|
| Movement feel | 7/10 | 8/10 | 8/10 |
| Visual fidelity vs Lost Crown | 4/10 | 6/10 | 6/10 |

Visual fidelity will not move much further with code. What separates it from
Lost Crown now is art direction: no particles, no weather, kit assets rather
than authored art, and a generic run cycle rather than a character whose walk
says something about them.

## Three defects a person found in one sitting

None of them was caught by forty green checks, and the reason is the same in
all three cases: the suite looks at single frames and at state, never at what
the game feels like over a few seconds.

**Animations did not loop.** Every KayKit GLB imports with `LoopMode.None`, so
`Running_A` stopped after 0.80s and the model held its last pose while the body
kept moving -- the character appeared to slide. Invisible to this suite by
construction: its captures are single frames, and a clip that has stopped looks
exactly like one that is playing. Fixed for enemies too, which build the same
libraries from the same files.

**There were no attack animations, because there are none to play.** The free
KayKit Adventurers pack ships two animation libraries covering movement, hits
*taken*, death and item use. No swing; those are in the paid tier. `ClipFor`
had no attack case because there was no clip to name. The swing is now
procedural: the weapon rotates on a pivot under the hand bone -- winding back,
then through an arc -- and the model leans into it.

**Finishing the run led nowhere.** The last exit emitted `RunCompleted`, a
banner appeared, and nothing else happened: no restart, no menu. Reported as
"there is no way to finish the room", which was accurate. The save from that
session recorded `RunCompleted = true`, so the ending had been *reached*; it
simply did not resolve. Jump now starts a fresh run and wipes progression and
the save.

Fixing the last one turned up a second defect underneath it: the restart read
`Input.IsActionJustPressed`, which is true for exactly one frame, so whether it
fired depended on whether that node's `_Process` ran before or after whatever
produced the press. Traced: 300+ process ticks with the button never once
observed as pressed. The edge is now tracked locally.

**The gap itself is now checked, not just the three bugs.** Asserting that
`LoopMode` is set tests the fix, and a check written against a fix cannot catch
the next way the same thing breaks -- a clip that loops but is never advanced,
a paused `AnimationPlayer`, a state machine that stops calling `Play`. The animation-motion check
watches the skeleton instead: it holds run for three seconds and compares pose
change per frame in the first second against the third. With the fix removed
those read 0.055 rad and **0.00000 rad** -- the frozen pose, reproduced as a
number.

A suspicion that did *not* hold up is worth recording too. The same session
banked only the checkpoint at the spawn, which suggested the later ones were
unreachable -- every death would then restart the hardest room in the game.
Measured by ray: all five checkpoints in room 5 have floor beneath them. Check
42 keeps it that way, but it did not find the fault, because there was none.

## Making an attack readable

`EnemyState.Attack` mapped to the same `Throw` clip for all three enemy types,
because the free KayKit pack ships no melee swing. So an incoming blow had no
pose, no timing cue and no sound distinct from any other action -- it simply
happened. That is survivable in a game about dodging and fatal in one about
parrying, which is where this is heading.

The wind-up is now posed from bones, like the player's swing: the weapon arm
draws back across the whole wind-up, then snaps through on the strike. **And
the weapon glows while it draws.** That second part is not decoration -- at this
camera distance a raised arm on a character a few pixels tall is not a reliable
signal, and the cue peaks just before the blow rather than at the start, so it
brightens as the answer becomes due.

The attack-tell check measures all three parts separately, since they fail separately:
1.40s of wind-up observed, 60 degrees of arm movement, glow peaking at 3.33.
The glow is read off the weapon's own material rather than off a flag, so the
check fails if the overlay stops being applied at all -- which is exactly what
happened on the first attempt: **Godot forbids "." in node names** and silently
rewrites them, so looking the weapon up as `Attach_handslot.r` matched nothing,
every time. The arm moved, the glow measured 0.00, and only reading the
material caught it. The lookup now goes by bone.

## Parry

**K**, between the two attack keys, where a hand already is. Dash gave up its
K binding and keeps Shift.

A press raises a guard for **0.38s**, of which the first **0.14s** is perfect.
The window is tuned against the tells it has to answer -- a melee wind-up runs
0.35s and the sentry draws for 0.55s -- so reading the tell is rewarded and
mashing is not, while the window is still reachable once the arm draws back.
A guard is **consumed** by the blow it turns: one press answers one attack, not
a whole exchange, and a 0.52s cooldown stops it being held permanently.

| | Damage | Attacker |
|---|---|---|
| No guard | takes the hit | untouched |
| Guard, past the perfect window | none | untouched, and the player is shoved back |
| Guard, inside it | none | **staggered** |

The shove on an ordinary parry is deliberate: it costs ground, which is what
makes the perfect one worth aiming for rather than a strictly worse dodge.

Two implementation notes worth keeping. The parry is checked **before anything
else** in `TakeDamage` -- health, knockback, stun and the hurt state have to be
skipped together, since a parry that stopped the damage but kept the stun would
read as a parry that did not work. And the punish is a **broadcast**, not a
call on the attacker: `DamageInfo` carries a source position rather than a
source node, and widening it to suit one mechanic would push a combat detail
into every call site that deals damage. Enemies within 3.2 units of a perfect
parry stagger themselves, and unsubscribe on `_ExitTree` -- they are freed
constantly, on death and on every room rebuild, and a bus holding freed nodes
throws on the next emit.

Both parries have their own sound, and they are separated by **noise versus
tone** rather than by pitch: the ordinary one measures a 7410Hz centroid at 0.34
noisiness -- a harsh broadband clash -- and the perfect one 1536Hz at 0.07, a
clear ring. That is the opposite of what was aimed for and the better of the
two, and it is only known because the sound-separation check measures every cue against every
other one. Knowing by ear matters here: the tight window is worth aiming for
only if you learn immediately whether you hit it, and looking down at the blade
to find out is exactly the moment you cannot spare.

The blade shows which is which: it comes up across the body either way, glowing
**white** while the perfect window is open and **amber** after it closes. That
colour is the only thing separating "you are safe" from "you are safe and about
to punish".

## Settled: the whole run is playable

For most of this project's life the honest answer to "can the game be finished"
was "the first half, and we do not know about the rest". It is now six of six,
deterministic to the frame across three runs, with real button presses and the
real exit trigger.

| Room | Frames | Climb needed / achieved |
|---|---|---|
| 0 | 650 | 4.3 / 4.6 |
| 1 | 906 | 7.3 / 8.5 |
| 2 | 527 | 4.6 / 4.6 |
| 3 | 665 | 6.7 / 7.0 |
| 4 | 863 | 10.8 / 12.0 |
| 5 | 831 | 5.6 / 6.5 |

Rooms 3-5 run at saturated difficulty and had never been crossed. Two changes
closed the gap, and only one of them was about the bot: the chimney became a
rightward staircase instead of a stack of shelves -- a **level** fix, prompted by
a play report of "platforms too close together, three of them one above the
other" -- and the bot stopped dashing into the void at the end of a room.

That is worth keeping in view: the thing that made the level finishable was
reported by a person looking at it, not by any of the forty-six checks.

## The same mistake, twice, four months of work apart

`AudioStreamPlayer.Playing` never goes false for an `AudioStreamGenerator` --
the stream does not end, it runs out of buffered frames and emits silence -- so
after the first eight sounds every voice in the pool looked permanently busy and
every later sound stole one. Found in a real playthrough's log: twelve sounds,
five steals, with the events seconds apart. Voices are now reserved for the
actual duration of their sound.

**The fix was written, and then left unverified.** A test for it was written at
the same time and never run and never added to the gate. When it was finally
run it FAILED -- 8 steals on sounds it believed were a third of a second apart --
and the reason was not the pool. It spaced them **20 frames** apart; under
`--fixed-fps` in headless a frame is about a millisecond of wall time, so twenty
of them are twenty milliseconds and every voice was still legitimately playing.

That is the "frames are not seconds" trap, which this README has carried as a
written lesson since the capture harness hit it. Writing a rule down does not
stop you making the mistake; running the test does. Spaced by wall time the
count is 0, and a burst of 16 in one frame still steals 4 -- both directions
asserted, because a pool that never steals is one that drops sounds.

## The HUD stopped looking unfinished

The ability icons were flat `ColorRect`s with the ability's name underneath --
the same "no art here yet" impression the pickups gave before they became
potions. They are now **drawn**: two chevrons for the double jump, three
streaks for the dash, a wall with a figure leaving it, a blade with impact
lines. Drawn rather than textured because the free KayKit packs contain no UI
art, and four small line glyphs cost nothing, scale cleanly and stay readable
over any background -- which an imported sprite at 40 pixels would not.

Two things the first attempt got wrong and a look at the capture caught. The
icons took the width of their own labels, because a `VBoxContainer` stretches
its children, so the row read as four different sizes -- they are pinned square
now. And the wall-jump glyph drew the wall in the same weight as the arrow, so
the whole thing read as a tick.

**The colours were written out twice**, once for the crystal on the floor and
its light, once for the icon in the corner, so the two agreed only by
coincidence. `AbilityLook` is now the single place that decides what an ability
looks like, colour and prop both. Removing the duplication is better than
adding a check that the two copies still match.

## The suite was blind to everything drawn

Almost every check here runs `--headless`, which has no renderer. That was a
deliberate trade -- headless is fast, deterministic and enough for logic -- and
the cost of it was never measured until a bug made it obvious.

Bone posing feeds its own output back through `Quaternion.Slerp` every frame.
The product of two unit quaternions is unit in exact arithmetic and only
approximately unit in floats, so drift accumulates, and `Slerp` throws on a
non-unit quaternion. In a windowed run of the bot playing all six rooms that
was **119 engine errors**. The same bot, the same rooms, headless: **zero,
every time, on every attempt**.

So the suite had no eyes on the entire visual layer -- skeletons, the animation
player, anything drawn -- which is precisely the class of defect a person kept
reporting and forty-eight checks kept missing: the sliding run cycle, the
invisible sword, the placeholder cubes, the stacked ledges. Every one of those
was found by looking, none by the gate.

The renderer check closes it: 4600 frames of the bot playing with a renderer, asserting
only that the engine printed nothing. **2600 frames was tried first and passed
with the bug still present** -- the number is written down because it was
measured, not chosen. With the defect restored it reports 51 errors and fails.

## Rebuild cost, and two ways of fooling myself

Frame times had only ever been measured on captures that teleport. With a bot
that plays the whole run, the real number was available for the first time --
and correlating the worst frames with the events near them put four room
transitions in the worst fourteen, at 100-170ms each. A visible freeze every
time you walk through a door.

**The first phase timing was dominated by the timing itself.** "platforms:
233ms" of a 450ms build, and the loop it measured contained a `GD.Print` per
platform that only runs when the metrics flag is on. `GD.Print` to a console
costs far more than placing a platform. With the dump moved behind its own flag
the same phase measures **0.3-3.9ms**. The instrument was most of the reading.

What the honest timings then showed:

| Phase | Before | After |
|---|---|---|
| platforms | 3.9ms | 0.3ms |
| enemies | 90-116ms | 16-69ms |
| **total** | **~450ms** | **~155ms** |

Two changes. Structural pieces -- floor tiles, the blocks under raised ledges --
go through a `MultiMeshInstance3D` instead of one instantiated scene tree each;
they never move, never animate and carry no script, and collision was always
separate box shapes rather than the imported meshes. And the KayKit animation
libraries are loaded **once** rather than per character: every enemy used to
instantiate both animation GLBs and deep-copy their libraries, which is most of
what spawning three of them cost. Sharing them also means the loop-mode fix
cannot drift between the player and the enemies, because it is applied in one
place.

**The second way of fooling myself was measuring once.** A single run after the
change read p95 27.88 and p99 47.22 and looked like a regression. Three runs
read p95 17.4-20.7 and p99 23.3-26.4. The first was a cold cache. The
"before" figure it was being compared against -- p99 32.21 -- was also a single
run, so it was never a sound baseline either.

## Spreading the rebuild

Making the build cheaper was not enough on its own: 155ms still lands on the
single frame the player crosses a door, which is nine dropped frames at exactly
the moment they are looking at the screen.

The build is now a sequence of steps rather than one method. `RebuildAs` runs
them all immediately -- the first room needs that, and so does every check,
which asserts on the result and would otherwise be racing a build. `BeginRebuild`
runs **one step per frame**, and the door uses that. The player is pinned in
place while it runs, because with the floor not yet placed it would fall through
the world during the frames the build takes.

| | Frames over 33ms in a played run |
|---|---|
| Before | 117 |
| After | 13-23 |

p99 fell from 23.3-26.4ms to 17.9-25.6ms across three runs.

**It broke three checks, and the breakage was the right kind.** `EnemyDamage`,
`RunArc` and `EnemyMix` assert immediately after crossing a door, and for a few
frames the room now exists while its enemies, pickups or exit do not -- so they
read a half-built room and reported the content as missing. That is a genuine
new state the game has, not a testing artefact, and the fix is for them to skip
those frames rather than for the game to pretend the state does not exist.

## Looking at a played run, and three wrong readings before the right one

The renderer-aware check exists to keep the engine quiet; it also makes it
possible to *look* at a whole run for the first time. Doing so found the raised
ledges reading as untextured blocks. Getting from that impression to the cause
took three wrong readings, each corrected by a sharper measurement:

1. **"They look darker."** Averaged over a wide band, ledges measured 73.1
   against 77.8 before the change -- 6%, inside the difference the two captures
   had anyway. Conclusion: no regression. The band was mostly wall.
2. **Sampling the ledge pixels themselves** told the real story: R44 G46 B47 at
   brightness **45.6**, against warm stone at 94-114 everywhere else. Shadow
   darkens and keeps the hue; this had no hue at all.
3. **"Then it is the MultiMesh."** It is not: the floor tiles go through the
   same batch and stay warm. The prop was simply wrong -- KayKit's
   `floor_foundation_allsides` samples a neutral grey from the atlas by design.

The first fix attempt swapped in the `wall` piece, which is the room's own
stone and four units wide, and it swallowed the screen. The second tints the
foundation warm on that batch alone: R126 G91 B61 at brightness 92.6, level
with the floor at 91.9.

Worth noting against the earlier entry that removed `MaterialOverride`: a
blanket override forcing surface 0's material onto every surface of every batch
was wrong, and a deliberate overlay on one batch for a stated reason is not the
same thing.

## Climbing out of the built world

The corridor wall ran from -4 to +4 and stopped, and every torch sat at
y = 2.4. So a player climbing a chimney rose out of the lit, built part of the
room into a band with no structure and no light: measured on a played capture,
the frame drops from about 110 brightness at floor level to about 60 the moment
you leave it. In a platformer, not being able to see the ledge you are aiming
at reads as the game being broken rather than hard.

Three wall courses now instead of two, and a **sparser** second row of torches
above -- sparser on purpose, so the corridor keeps pools of light rather than
becoming an even wash. The upper band measures 94.7 against 59.8 at the same
pixel rows.

The back wall moved to the batch at the same time. It is dozens of identical
pieces per room and was part of what made rebuilds expensive; adding a third
course without batching would have given back the performance just recovered.

**One number in that comparison is not evidence.** The lower band reads 109.9
before and 64.6 after, which looks like a regression and is not one: the two
frames do not show the same geometry at those pixel rows, so a fixed-row
comparison between them is meaningless. The upper figure is directional; the
capture is the evidence.

This was the third hasty visual reading of the session, and like the other two
there was something real underneath it -- just not what it looked like. "A wide
black band across the screen" was actually a contrast step, and the vertical
profile has no hole in it at all: 35 to 113, continuous.

## A fourth enemy, and what the roster was actually missing

The complaint was "not much enemy variety", and the shallow reading of that is
"add more". The real problem was that each type had exactly one answer: walk up
and hit the grunt, close on the sentry, wait out the skirmisher. Adding a fourth
with the same shape would have changed nothing.

`Warlock` **holds** its range instead of standing in it. It gives ground while
you approach and stops only when it has the distance it wants, so closing costs
you the ground it keeps taking back. It is the most fragile thing in the game
(18 health) and the most painful (14 damage), with the longest tell (0.70s) and
the slowest cadence (2.0s): reaching it *is* the fight.

It is also the first type that uses `ChaseSteering` for what the hook was built
for -- choosing WHERE to be rather than how fast to get there. `Skirmisher`
retreats only while recovering; this holds a stand-off continuously. It appears
from **room 2** onward, because meeting one before the dash exists is a fight
with no answer rather than a hard one.

Its check is a **different measurement** from the skirmisher's, because its
signature is a different thing: not travel after a hit but the gap it keeps at
all times. Average distance held, sampled every frame: **4.4 against the
grunt's 1.6**. Neutralising its steering override collapses it to 1.6 and the
check fails.

**The roster test had gone stale and said so loudly.** It counted three types
from a hardcoded list, so a room full of warlocks counted as zero enemies and
the check reported an empty room. It now counts by concrete type name, so it
does not have to be edited every time the roster grows -- which is exactly when
a roster test being wrong matters most.

## The room had no sound of its own

Between hits the game was silent, which does not read as quiet -- it reads as
switched off. There is now a bed: a low drone built from two partials a couple
of hertz apart, so the beat between them sounds like a space rather than a test
tone, and a rare water drip on top. Both are deliberately dull. An ambience you
notice is one you will be sick of by room three.

Two things it does not do. It does not use the eight pooled voices -- those
exist for gameplay cues, and a drone holding one permanently would starve them
exactly the way the never-released-voice bug did. And it does not end: the
drone is pushed in chunks as its buffer drains, so the failure mode is not
silence but "two seconds and then nothing", which is indistinguishable from
working code in any check that is too short.

**The check got that wrong on its first try, in the way this project keeps
getting it wrong.** It counted frames: 680 of them, which it treated as eleven
seconds, and saw a single refill. Under `--fixed-fps` in headless those 680
frames are about one second of wall time, and the generator drains in real
seconds whatever the engine's step is doing. Timed properly it reads 18 refills
in five seconds. That is the third time "frames are not seconds" has cost
something here, and the second time in this file.

## The intermittent, caught at last

For most of this project's life a check would fire on a couple of consecutive
gate runs, name engine errors, and then pass twenty times in a row. It was
recorded as unresolved rather than quietly rerun until green, and the gate was
armed to append the offending output to `tools/.engine-errors.log` the next time
it happened. That is what finally caught it:

```
ERROR: Leaked unsafe reference to object: <PhysicsRayQueryParameters3D#...>
   at: finalize (modules/mono/csharp_script.cpp:179)
ERROR: FATAL: Condition "csharp_lang && !csharp_lang->script_bindings.is_empty()" is true.
```

449 of them in one run. `PhysicsRayQueryParameters3D.Create` allocates a
RefCounted Godot object from C# on **every cast**. The enemies' ledge and wall
probes run twice per physics frame each, and the traversal bot casts five more
per frame on top; a long run accumulates them faster than the finaliser clears
them, and at shutdown the binding table is still holding thousands. Both now
reuse a single query object and mutate its endpoints.

**Why it hid for so long.** It needs a run long enough to outpace finalisation,
so every short check passed and the long ones only tipped over sometimes,
depending on how much else the process had to clean up. It is exactly the shape
of bug that a suite of quick, isolated checks cannot see -- the same blind spot
as the renderer, one layer down: not "we do not look at this", but "we do not
run long enough for this to exist".

Three consecutive runs of the check that caught it now report zero.

## Measured, but not settled

**Two chunks are still unproven in isolation.** The per-chunk check builds each chunk kind
alone at saturated difficulty; five of seven clear, and Chimney and DashGap are
excluded. Both now cross fine inside a real room -- the full run is six of six --
so the exclusions are about the bot meeting them cold with no run-up, not about
the chunks. They stay excluded rather than asserted, because a check that
passes for a reason you have not established is not a check.

To narrow it, each chunk kind was built alone at saturated difficulty and run
the same way. Five of seven clear:

| Chunk at intensity 1.0 | Result |
|---|---|
| StepUp, Gap, Spikes, Arena | crossed, 170-560 frames |
| WallShaft | crossed in 262 frames — the tallest thing the generator builds |
| DashGap | the bot falls in; ends at y = -4.3, below the level |
| Chimney | climbs 0.1 of 3.7 — and presses attack 91 times, so it is standing at the foot fighting a grunt rather than climbing |

Neither failure is evidence of an unclearable chunk: one is a bot that mistimes
a dash, the other a bot that would rather fight than climb. But neither is
evidence they *are* clearable, so both are excluded from the check rather than
asserted. What remains genuinely unresolved is whether some combination in
rooms 3-5 cannot be done, and settling that needs a better bot or a human.

The traversal check and the per-chunk check both assert only what the bot demonstrably does:
asserting more would encode how good this bot is, not whether the game is
playable.

## To do next, in order

Everything here is known and unstarted, not forgotten.

1. ~~**Parry, and perfect parry.**~~ **Done.** See below. What is left is
   tuning by feel, which needs a person: the numbers are defensible but only
   playing says whether 0.14s is generous or mean.
2. **Elevated ledges still show a seam** between their top face and the
   underside plate. Cosmetic, visible when climbing.
3. **Rooms 3-5 are not proven traversable.** See "Measured, but not settled".

## A menu that existed and was connected to nothing

Pressing pause froze the world and showed nothing, with no way out but pressing
it again. Not because the menu was missing -- `PauseMenu.tscn` had existed for a
long time, with Resume and Quit, correct and working. It was only ever placed in
a **UI test scene**, never in `Main.tscn`.

That is this project's recurring failure in its purest form: a thing that
exists, is right, and is wired to nothing. It is the same shape as the ability
that was never granted, the checkpoint bookkeeping with no consumer, and the
save field with no producer. The lesson each time is that "it works" and "it is
reachable" are different claims, and only the second one matters to a player.

It is now in the game, with a **Restart run** button that shares the same path
as the run-complete banner, so "start over" means one thing however it is asked
for. The check runs against the real `Main.tscn` -- one that instanced the menu
itself would have passed throughout.

Two things the check got wrong first, both worth keeping:

- `Input.ActionPress` sets an action's **state** but produces no `InputEvent`.
  A menu has to read `_UnhandledInput` -- polling would fire every frame the key
  is held -- so it never saw the press. The player controller polls, which is
  why one responded to the synthetic input and the other did not.
- A test that pauses the tree **stops processing itself**, so it never reaches
  the frame where it would unpause. It hung silently rather than failing, which
  reads as slowness long before it reads as breakage.

## Two actions on one key, found by writing them down

The game booted straight into room 0. No title, no way to leave before
entering, and nowhere the controls were ever stated -- a player had to be told
the keys out of band.

There is now a title screen: Play, Quit, and a list of the bindings. The list
is **built from `InputMap` at runtime**, not typed into the scene. A typed list
is a second copy of the bindings that nobody updates, and it keeps naming the
old key long after the binding moves.

Reading the bindings back is what found the defect. `parry` and `attack_light`
were **both bound to mouse-left**, so every click did both: the parry opened
and the swing started on the same press. It is invisible in code -- each
handler reads its own action and each looks correct -- and shows up only when
the two are listed side by side. Parry's mouse binding is removed; the mouse is
now left-light, right-heavy, and parry is K. The check asserts no two actions
share an event, so it cannot come back.

The check reads the scene path from `ProjectSettings` rather than naming it,
which is the entire point: a title that exists and looks right but is not what
`run/main_scene` points at is invisible to the player. Hardcoding the path
would have passed while the game still booted into the dungeon -- proven by
pointing `run/main_scene` back at `Main.tscn`, which fails with *"the game does
not boot into a title screen"*. It also asserts the world is **not** running
behind the title: a menu drawn over a live room still shows a menu, and is
still wrong.

One engine detail cost eight errors per boot: `DisplayServer.KeyboardGetKeycodeFromPhysical`, which is what makes the label right on a non-US keyboard, is
**not supported by the headless display server** and logs an error per binding.
Harmless on screen, fatal at the gate -- the engine-quiet check fails any run
that prints one. Guarded on the display server's name, with the untranslated
key name as the fallback.

## The three decisions, all closed

These were the only things that were choices rather than work. All three are
now decided, two of them by you.

1. **The game had no arc and no ending.** A run is six rooms and then it ends:
   the last exit fires `RunCompleted` instead of rebuilding, the HUD says so,
   jump starts a fresh one, and the save stops claiming the run is finished.
   Six because the difficulty ramp saturates at index 3 -- three rooms of
   build-up, three at full difficulty, each layout seen twice. `RunLength` is an
   `[Export]`, so changing it is tuning, not a rewrite.

2. **Two room systems existed, one unused. Deleted.** The authored-scene half
   is gone: autoload, graph, three blockout rooms, the two scripts only they
   used, and the check that exercised them. The proof it was self-contained is
   that removing it left a build with zero errors and zero warnings. Async room
   loading went with it; the hitch it would have hidden was measured and fixed
   by spreading the rebuild across frames instead.

3. **One enemy type. Four now, and each demands something different.** The
   shallow reading of "not much variety" is "add more"; the real problem was
   that every type had one answer. A grunt you walk up to, a sentry that has to
   be closed on through fire, a skirmisher that will not stand and trade, and a
   warlock that holds its range so closing costs you the ground it keeps taking
   back.

## A boss, and the first time the parry is the cheapest option

Four enemy types and no boss meant the run had no shape at the end: the sixth
room was the fifth room with more in it. Worse, the parry -- the mechanic with
the most code behind it -- was never the cheapest way to win anything. Every
type in the roster is beaten by movement: walk up to the grunt, close on the
sentry, wait out the skirmisher, take back the ground the warlock gives up.
All four answer to the same verb. A player had no reason to ever learn the
other one.

The Warden is **armoured while it is doing anything**. A blow that lands on
armour is cut to 2 damage and, just as importantly, **does not interrupt it**
-- without that second half a player could stagger-lock it on 2 damage a swing
and the parry would stay optional. It opens only when a perfect parry staggers
it, and then it is fully open for 1.6 seconds, which is one charged heavy or
three light swings. Measured: 55 damage on armour removed **2 hp**; the same 55
during the stagger removed **55**.

Chip damage is deliberately not zero. A boss that is literally invulnerable
outside one window reads as broken rather than hard, because nothing on screen
tells "absorbed" apart from "the hit did not register" -- so a player who never
lands a parry still wins, in about sixty hits. The HUD bar says which state it
is in: grey while absorbing, lit while open. Below half health it enrages,
throws at range, and commits faster, so standing outside its reach stops being
an answer.

Three things this turned up that were not the boss:

- **A player standing in the doorway when the boss died stayed locked out.**
  `BodyEntered` does not fire again for a body that never left, and the doorway
  is exactly where the fight's last knockback puts you. The exit now re-checks
  what is already inside it, over twelve physics frames rather than one --
  in `_PhysicsProcess`, because in `_Process` the overlap answer is a frame
  stale, which is long enough to miss.
- **The sixth vacuous assertion.** The enemy-mix check drew its "a bolt was
  airborne" evidence entirely from the long dwell in room 5, and room 5 is now
  a melee boss. It did not fail: it went quiet in the shape of a pass, because
  "0 bolts left after teardown" is trivially true when no bolt ever existed.
  It now steps back to the last room that contains something that fires.
- **A blunt 100000-damage kill removed 2 hp.** The run-arc check kills the boss
  to test the ending rather than fighting it, and the armour absorbed the
  number. That is the armour working, and it took a trace to see because the
  number looked too decisive to question.

## The camera punch had never been visible

A perfect parry is the hardest thing in the game to do and, until now, the
quietest: a sound cue and nothing on screen. Adding a camera punch to it turned
up that the one already there did not work either.

`ShakeDecaySpeed` was 6.0, applied with `MoveToward` — a LINEAR decay in units
per second, so 0.1 per frame at 60fps. The landing punch is 0.08, which means
it was gone **in less than one frame**, and since the camera lerps toward its
desired position, a single-frame offset barely moves it. Measured on the parry,
which starts at 0.22: two frames after the blow it read **0.020**. At 1.2 the
same punch lasts about 0.18s and reads as an impact.

Impacts also leave a mark now: red and wide for a blow that landed, dull and
small for one the boss's armour ate, gold for a perfect parry. The second one
is not decoration — it is the answer to the boss's own design problem, that on
screen "absorbed" and "the hit did not register" were the same picture, and a
player cannot learn a rule they cannot see.

The check asserts the two colours **differ** rather than what they are. Pinned
to specific values it would fail on a palette tweak and pass on the only change
that matters: the two collapsing into one.

## Rebinding, and the rule that a key belongs to one action

The title screen could list the bindings but not change them, which is half a
menu. There is now an options screen — volume and every gameplay key — reached
from both the title and the pause menu, and it is the same node instanced
twice rather than written twice.

Its rows are built in code, one per entry in `InputSettings.Rebindable`. A
scene with eight hand-authored rows would be a second copy of that list, and
a second copy of the bindings is exactly what was avoided on the title screen
for the same reason: the copy stops matching and keeps naming the old key.

**Rebinding onto a taken key is refused, not swapped.** A swap looks helpful
and silently moves a binding the player was not editing. A refusal names what
is in the way. This is the same rule the title's check enforces on the project
file, now enforced on anything the player can do by hand — parry and light
attack sharing mouse-left was not a typo, it was the absence of this rule.

The check reads the binding back **from disk** after a reload, which is the
half that matters: a rebind can be applied to `InputMap` and never written, and
nobody finds out until the next launch. Proven by deleting the two lines that
save it — the reload then reports Space, and the check fails.

## The project is under version control

As of this commit it is a git repository, pushed to GitHub. Until then every
change was irreversible -- raised repeatedly, including when an entire unused
room subsystem was deleted on the strength of a build with zero warnings.

The engine binary (258 MB), the raw CC0 asset packs (119 MB) and the playtest
frame dumps (35 MB) are excluded, with the reason written next to each rule in
`.gitignore`. What is committed is 5.4 MB: the source, the scenes, the imported
assets the game actually loads, and the docs.

## What is genuinely open

- **Tuning by feel.** The parry's 0.14s perfect window, six rooms, the
  difficulty ramp -- all defensible on paper, none confirmed by playing.
- **Chimney and DashGap are unproven in isolation.** Both cross fine inside a
  real room, so the exclusion is about the bot meeting them cold with no run-up.

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
