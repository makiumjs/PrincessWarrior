# Engineering log

Every defect this project has found, in the order it was found, with what it
measured. `STATUS.md` says what state the game is in; this says how it got
there.

The entries are not a changelog. Each one exists because something was believed
and turned out not to be true, and the measurement that settled it is the part
worth keeping -- a defect described without the evidence that revealed it is
indistinguishable from a guess.

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

## Three defects a playthrough found, and a fourth underneath them


### Enemies pointed the wrong way

Reported as "enemies strike in the opposite direction". The mechanism is
narrower and worse than that: damage is applied on RADIAL distance and a bolt's
direction is computed from the player's position, so **the hit always landed**.
What pointed the wrong way was the model.

Facing was driven by velocity. `TickAttack` zeroes horizontal velocity so the
enemy commits to its swing in place, and a sentry or a warlock holding its
range never moves at all -- so both kept whatever facing they last had. Nothing
in the suite could see it, because every assertion in it was about damage. A
tell that points the wrong way is worse than no tell: it is the one piece of
information a parry is timed against.

Measured with the fix removed: a melee enemy mid-windup with the player behind
it stays at facing `1`; a stationary sentry with the player on its left stays
at `1`.

The fix keeps the existing `scale.X` flip rather than forcing a Y rotation.
`EnemyVisual` applies a 90-degree yaw offset to the model, and a forced
rotation would fight it.

### The floor had no underside, and the holes were dark

Two causes behind one complaint. A walkable surface was a 15cm plank with
nothing beneath it, so every edge showed a thin line and then background; and
the torches sit at y=2.4 with nothing below them, so the space a player has to
jump across was the darkest part of the frame.

**The first attempt was wrong because the piece was assumed rather than
measured.** `floor_foundation_front_and_sides` is 2.2 wide and 2.0 tall with
its origin at its BASE: on a 4-unit grid it left a 1.8-unit hole between every
block and stood 1.5 metres ABOVE the floor it was meant to be under. A probe
that prints the AABB of every kit piece settled it -- `wall` is 4x4x1 with its
origin at its base, exactly one grid cell.

Two more things the captures caught:

- The foundation at z=+1.5 was **casting the directional light forward onto the
  player and the enemies standing on it**, visible as a grey band across a
  knight. The camera is orthographic, so depth does not move anything on
  screen: at z=-0.5 the face covers exactly the same pixels and shades nothing.
- Batch tints are keyed **per mesh**, and the foundation is made of the same
  `wall` piece as the backdrop -- so tinting it was silently tinting the entire
  back wall of every room. It has its own batch key now, and is deliberately
  *darker* than the backdrop: in a side-scroller the walkable surface has to be
  the brightest thing near it.

Mean frame brightness went 76.1 to 88.0 across the same three captured frames.

### The run was two minutes long

Measured first: a room was **64 to 72 metres**, not the ~50 assumed, crossed in
10 to 24 seconds. Six of them is a demo.

Each layout now runs about **200 metres in three movements** -- introduce,
recombine tighter, run home -- and a run is **ten rooms**. Descents were added
as a chunk kind for a specific reason: built only of ascents, the tripled rooms
climbed past 30 metres and turned into staircases, and a staircase is one idea
repeated rather than a place. The backdrop now follows the room's real vertical
extent instead of stopping at a fixed height, with a torch row per course.

| Measured | Before | After |
|---|---|---|
| Room span | 64-72m | 191-211m |
| Rooms per run | 6 | 10 |
| Enemies per room | 1-4 | 4-8 |
| Vertical rise | 4-12m | 5-17m |
| Bot traversal | 6 of 6 | 10 of 10, 42s |
| Frame time, played run | p95 17.4-20.7, 13-23 over 33ms | p95 18.1, 24 over 33ms |

The traversal bot's per-room budget was a flat 1600 frames, which cut five of
six rooms off between 92% and 99% -- that reads as "the level is impassable"
and means "the stopwatch was short". It is proportional now: 22 frames per
metre, three times the measured rate of 7.2.

**Arenas are sealed until they are cleared.** The Arena chunk exists to teach
the charge attack and was the easiest ground in the room to sprint past -- the
widest flat run, with the enemies standing in the middle of it. Its check
asserts the barrier LIFTS as much as that it blocks: a gate that never opens is
worse than no gate, and a check written only around "the player is stopped"
would call that a pass.

### Underneath: enemies never knew where they were

The corpse-cleanup check failed on a body at **(98.0, -93.7)** -- not one of the
twelve it kills itself, but an enemy from the room, fallen out of the world.
Its patrol anchors read `A=-3.0 B=3.0`. So did every other enemy's.

The builder did `AddChild(enemy)`, then wrote its position, then wired two
marker nodes. **`_Ready` runs on the first of those three**, so the enemy took
its "3 metres either side of me" fallback while it was still at the origin, and
never read the markers at all. Two nodes per enemy for a value that never
arrived.

It survived because it was survivable: at 64 metres, walking toward x=-3 meant
ending up near the start. At 200 metres they set off across the level -- three
of eight walked out of the world in a single run, and one stood on a spike trap
until it died.

Two independent fixes, either sufficient: the builder now says it directly,
after the position is real, and the fallback is measured on the first physics
tick rather than in `_Ready`. Proven by removing both -- enemies at x=16, 19,
46 and 72 all aiming at -3.

Spikes damaged enemies as well as the player. **Patrol now refuses a hazard;
chase still walks onto one.** That asymmetry is the design: an enemy that
wanders onto a trap unprompted is the level playing itself, one that follows
you onto a trap is a tactic.

And the corpse check itself was wrong. It asserted **zero bodies**, which was
right while the only deaths in the room were the twelve it caused; it now
asserts **no body older than its own cleanup budget**. It was failing on a
corpse 3.23 seconds into a 4.40-second cleanup, working exactly as designed.

### Two limits of headless checking, found here

- **A MultiMesh's buffer is empty under the headless renderer.** `InstanceCount`
  reads 15 and `Buffer.Length` reads 0, because the transforms live on the
  rendering server and the dummy driver keeps none. No windowless check can
  verify WHERE batched geometry is -- only how much of it exists. Placement is
  checked against what the builder recorded placing; the count against the
  scene.
- **`AddChild` without `forceReadableName` renames a colliding child to
  `@GapTorch@2`.** Three torches placed, one found by a name-prefix lookup. A
  group is rename-proof and is what the check uses now.

## The game exists as a program now


`bash tools/export.sh` produces `build/windows/PrincessWarrior.exe` -- 185 MB,
double-clickable, no editor required. Until this it ran only from the editor
binary, which meant nobody who was not sitting at this machine had ever seen
it.

**The export had been failing silently for as long as it existed**, and how it
failed is the part worth keeping. Godot's .NET export needs a
`LostCrownlike.sln` next to the `.csproj`. This project was always built with
`dotnet build` on the csproj directly, so no solution was ever created -- and
without one Godot still **wrote an exe and a pck**. That binary launched,
showed nothing, exited 0, and the equivalent debug build crashed with
**signal 11**: the assemblies had never been published.

So the check does not read the export command's exit code. It asserts that
`LostCrownlike.dll` is in the output, and that the binary starts with its C#
autoloads running -- `AudioManager: ready, 8 voices allocated` is printed from
managed code, so its presence is proof the runtime came up rather than proof
the engine did.

A second trap, recorded because the message points the wrong way: the export
path's directory must already exist, and Godot reports that as *"the specified
export path does not exist"*, which reads as a bad path rather than a missing
folder.

Two things the export cannot check, and does not pretend to: the release and
debug templates both refuse a scene path on the command line, so the exported
binary can only be started at its title screen. Driving it through a run needs
a person.

The templates themselves are ~1.2 GB and are **not** in the repository; the
header of `tools/export.sh` has the three commands that install them.

## A twelve-minute run needed a way back into it


The title has two doors now: **New run** and **Continue**, the second offered
only when the save has progress that is not a finished run.

The interesting half is the first door. "Play" was never a new run: SaveManager
re-announces the saved abilities on boot, so pressing it opened room 0 **with
DoubleJump and Dash already in hand and four rooms counted** -- the old run
with the level reset. Measured exactly that way with the fix removed. It wipes
the save before loading now.

The resume point crosses the scene change on `SaveManager.ResumeFromRoom`,
consumed once by the room builder. It lives on the save autoload rather than in
a static or on the bus because that is the subsystem that already owns run
progression, and because a signal emitted before the game scene exists has
nobody listening.

The check had to be built around two engine facts, both already paid for
elsewhere in this suite: it reparents itself to the root, because pressing
either button calls `ChangeSceneToFile` and that frees the current scene
(the first version went with it and printed two of its five lines); and it
reads the intent in the **same frame** as the press, because `Pressed` is
synchronous while the scene change is deferred, and one frame later the builder
has consumed the value.

One failure in it was the check's own: it asserted "Continue is hidden" against
a save that still had four rooms in it, because the scene's title node ran its
`_Ready` before the test could reset. It resets first and builds a fresh title
now -- which is also what relaunching the game does.

## Twelve minutes of one held chord is a hum, not a score


The dungeon had a drone -- deliberately dull, and right for what it is, which
is a room tone. It was written when a run was two minutes. At twelve it reads
as the game being unfinished rather than as the game being quiet.

There is a generated score now, on the same terms as everything else in this
project: no sample files, its own voice so the gameplay pool is never spent on
it, and quieter than the sword. A slow minor figure over a pad, walking the
scale rather than arpeggiating a chord -- a repeating arpeggio is most of what
makes generated music sound generated. Tempo and pad both follow how far the
run has got, and the boss drops the root and beats the pad harder.

**Checking that it moved found that it barely did.** The walk clamped to the
ends of its scale, and it started at degree 0, so every downward step was
truncated back to the bottom note: six seconds produced **two distinct
pitches**. Reflecting at the ends instead, and starting in the middle, gives
four to five across three runs. The clamp made the check flaky as well as the
music dull -- "at least three pitches" was a coin flip.

The check's first claim is the one a "there is audio" test would skip: a
generated stream that is not refilled **stops, silently, and stays stopped**,
which from outside looks like the buffer sitting permanently full. Proven by
deleting the push: the starvation counter reads 671 in six seconds.

## Three things the longer rooms broke, and one they did not


Tripling the rooms did not introduce these. It made two of them reachable and
proved the third was never true.

**Five useless crystals on the floor of room 0.** `SpawnAbilityPickups` reads
what the player owns ONCE, before its loop -- correct while a room held one of
each obstacle, and its own comment says so: *"so a later room does not litter
the floor with a second Dash crystal"*. It covers the across-rooms case. The
tripled layouts hold three DashGaps and three Chimneys, and every one asks for
its ability, so room 0 laid out **three Dash and two Double Jump**, and room 1
seven pickups where three would do. Measured exactly that way by reverting the
fix. The check that already existed for this ("an owned ability is not
re-offered") now also refuses the same ability twice in one room.

**The back half of the run was as dangerous as the front.** The difficulty ramp
scaled obstacle SIZE and nothing else, so ten rooms read 4, 5, 8, 4, 5, 8, 4,
5, 8 enemies -- the layout cycle repeating, not a run getting harder. From the
halfway room, every other encounter gets a second body. First half 5.5 enemies
a room, second half 9.2; with the ramp removed, 5.5 against 6.2, which the
check's 1.2x threshold correctly refuses. The traversal bot still crosses 10 of
10, in 50 seconds instead of 42.

**And one that was a guess.** The build was one step per frame, and a room went
from 15 platforms to 46, so the 144ms spike a played run still shows looked
like the platform step. It is not: slicing that step into twelves left the
maximum at 144.87ms against 144.65, and disabling the impact bursts left it at
121ms. Compared against the numbers from before the rooms grew -- 13 to 23
frames over 33ms in a played run -- **there is no regression**; those spikes
predate all of this and do not correlate with any gameplay event.

The slicing is kept, with that said plainly. It buys nothing today. What it
does buy is a bound: twelve platforms per step whatever the room's length,
where before it was however many the room had.

## The abilities all arrived in the first three rooms


A run hands out four abilities. Every layout opens with the chunks that grant
them, so a ten-room run gave Dash and Double Jump inside the opening forty
metres, Wall Jump in room 1 and Charge Attack in room 2 -- and then seven rooms
with nothing new in them. Fine at six rooms of seventy metres; flat at ten of
two hundred.

Rooms now build only the chunks they are allowed to, substituting rather than
reordering: a DashGap the player cannot yet cross becomes a Gap, a Chimney
becomes a StepUp. Each layout keeps its shape and still rises and falls where
it did; the obstacle still teaches its own ability, a room or two later. Room 0
has no crystal at all now -- it is the room that teaches jumping. The grants
land in rooms 1, 2, 4 and 5, and the vertical rise climbs 2.6, 4.0, 5.0, 7.1,
13.5 metres instead of starting at 5 and jumping about.

**The bot rejected the first arrangement, which is what it is for.** Dash first
and Double Jump third failed rooms 2 and 3, stalling exactly where the
difficulty ramp passes about 0.6. The metric contract sizes every gap so one
jump clears it -- true, and not the same claim as "clearable by a deliberately
clumsy player". The old order hid the difference by handing out Double Jump in
the opening forty metres. It is the ability that makes every other obstacle
forgiving, so it goes first. Reordered: 10 of 10, in 54 seconds.

### Four checks that remembered instead of asking

Gating, PickupSkip, WallJumpReachable and ArenaLock each hardcoded which room
held the thing they tested -- "room 1 has the wall shaft", "room 0 has the
crystals", "room 2 has the arena". All true until the gate moved them, and then
four red checks that were reporting a design decision rather than a defect.
They ask the builder now (`FirstRoomWith(kind)`), which is the same lesson as
the check numbering that rotted three times: do not remember what you can ask.

Two of them also depended on the save left behind by whichever check ran
before. "The player starts with abilities locked" is a claim about a fresh run,
and it was passing because Gating happened to be the fourth check, three places
after the one that deletes the save file. Both reset it themselves now.

### And underneath them, a real one

A player who slides off the BOTTOM of a wall keeps the wall-slide state. Traced
at y=-0.97, falling at -8.4 m/s and accelerating to -15, with `CurrentState`
still reading `WallSlide` -- a state whose entire job is to cap the fall at 3.
The pose stays too.

`ResolvePostMoveState` declines to overwrite "a state that was just explicitly
set this frame", and cannot tell that from one left over from the frame before.
So the state outlived the wall that justified it.

It had been there all along and was invisible: the wall shaft was in room 1,
short enough that the check's 200-frame window closed before the player reached
the bottom. **Moving the shaft to a later, taller room is what made the drop
long enough to show it.** Now -3.00 flat for the whole slide.

Two measurement faults were separated from that real one rather than folded
into it: the check sampled the frame the player ENTERS the state, which still
carries the previous frame's free-fall velocity, and it took the instantaneous
minimum rather than the sustained speed.

## The project is under version control


As of this commit it is a git repository, pushed to GitHub. Until then every
change was irreversible -- raised repeatedly, including when an entire unused
room subsystem was deleted on the strength of a build with zero warnings.

The engine binary (258 MB), the raw CC0 asset packs (119 MB) and the playtest
frame dumps (35 MB) are excluded, with the reason written next to each rule in
`.gitignore`. What is committed is 5.4 MB: the source, the scenes, the imported
assets the game actually loads, and the docs.

## Every chunk is proven alone now


Chimney and DashGap had been excluded from the isolation check for months,
with their measurements written beside the exclusion: the bot fell into the
dash gap, and at the foot of the chimney it stood fighting a grunt -- **91
attack presses** -- instead of climbing. The reason given was sound at the
time: asserting them would have encoded how good the bot is rather than
whether the game is playable.

Retried after the per-room budget became proportional to the room's length.
Both cross, in 177 and 219 frames. Drop, which is new, is included from the
start. Eight of eight.

One number in the pass says something the pass does not. The bot climbs the
chimney on **wall jumps, not double jumps** -- 2 wall jumps, 0 double jumps,
3.8 metres of the 3.7 it needed. The chunk built to teach Double Jump can be
answered another way, because the sides of its ledges are climbable surfaces.
Not a failure -- the claim is that each chunk is clearable with what it grants,
and it is -- but it is worth having written down rather than discovered by a
player.

## Everything emissive, and nothing glowing


The game had no `WorldEnvironment` for most of its life. The evidence was in
`project.godot`: a default clear colour set there as a workaround, with a note
explaining that Main.tscn had no environment to set one. What that also meant,
and nobody had connected, is that **every emissive surface in the game was
emissive and did not glow** -- the torches, the boss's seal, the arena barrier,
the impact sparks, the gold of a perfect parry. All of them declare emission.
Nothing was there to bloom it.

There is one now: warm ambient, ACES tonemapping, moderate glow, a touch of
contrast and saturation. Measured on the same captured frame, same seed:

| | before | after |
|---|---|---|
| Mean brightness | 80.2 | 89.2 |
| Warmth (mean R-B) | 25.4 | 36.5 |
| Near-black pixels | 8.0% | 10.8% |

Brighter AND darker, which is the point: that is contrast, not exposure. The
deeper blacks fall below the walkway, where they read as depth -- the holes
themselves are still lit, by torches placed for them, and that is asserted
separately.

Frame cost: none measurable. p95 17.80ms against 19.23 without it, which is
inside run-to-run noise on an RTX 3080. On a weak GPU glow is not free, and
this machine cannot tell you what it costs there.

The assertion went into the check that already asks whether the level can be
seen, rather than becoming a check of its own. Teeth: removing the node from
Main.tscn reports `present=False glow=False` and fails -- which is the failure
this project keeps having, a thing that exists in one scene and not in the one
that ships.

## Dust, and the half of the rule that matters


A landing raises dust and a dash throws a streak behind it -- both from the
same six shards the impact bursts already use, for the cost of two arguments:
one that squashes the fan toward the horizontal, one that pushes it one way.

**The threshold is the point, not the effect.** Dust on every touch of the
floor is worse than none at all: it stops meaning "that was a drop" and starts
meaning "you are standing somewhere". So it is gated on a fall SPEED rather
than a height, because speed is what the impact is, and the check asserts both
halves -- a 0.35m step raises 0, a 7m drop raises 1. Both mutations fail:
deleting the dust, and deleting the threshold.

The dash streak is thrown backwards along the direction of travel. It is the
only thing on screen that says how long the invulnerable window lasts, and that
window is the entire reason to press the button.

Two faults in the check, kept separate from the feature: it counted bursts
under the ROOM, but the player is a child of Main so its dust is parented
there; and it sampled a single instant when a 7m drop takes about 50 frames and
a burst lives 18, so one sample could land on the wrong side of both.

## The invisible exit, the invisible checkpoint, and corridors with no architecture

Reported from play: *"a livello di level design siamo messi male, piattaforme, porte ed altri elementi sono messi a caso e non c'e niente che indica il fine livello"*.

Four independent causes behind one impression:

1. **The exit was an invisible volume in empty air.** A player running down the
   corridor collided with a trigger they could not see and was abruptly teleported
   into the next room.
   *Fix:* An authored stone archway (`wall_doorway.gltf` stripped of its door panel),
   flanked by stone columns (`column.gltf`), swaying banners (`banner_red.gltf`
   with harmonic `BannerSway`), wall torches, and an animated dimensional portal
   vortex (`PortalVortex`, cyan pulse in normal rooms, royal gold for the final exit)
   with a 10m guiding light beacon (`OmniLight3D`). In the final room, a ruby
   energy seal (`ExitSeal`) physically and visually locks the arch until the Warden dies.

2. **Checkpoints were flat coordinates with no presence.**
   *Fix:* A sculpted plinth (`column.gltf` scaled at $Z = -0.6\text{m}$) bearing a
   hovering resonant crystal (`FloatingCrystal`). In standby, it pulses softly in dim
   blue (`LightEnergy = 0.9`); upon player arrival, it attunes with a radiant cyan/gold
   flare (`LightEnergy = 2.4`, `Emission = 3.4`), giving unambiguous visual feedback.

3. **Platforms floated without architectural support.** Walkways were single planks
   suspended in the void, and corridors were empty grey runs.
   *Fix:* `BuildPlatformColumns` places GPU-batched stone columns (`column.gltf`) under
   wide elevated walkways down to the floor, skipping narrow steps. `DecorateDungeon`
   places themed prop clusters (stacked crates, large barrels, chests, wall banners)
   along the back wall without polluting the movement plane.

4. **Monotony across ten rooms.** All ten rooms shared identical limestone textures and
   amber lighting.
   *Fix:* A 3-act progression across the 10 rooms:
   - *Act I (Rooms 0–3):* The Forgotten Crypts — warm limestone, golden amber fire (`1.0, 0.72, 0.42`).
   - *Act II (Rooms 4–6):* The Sunken Catacombs — damp mossy slate, bioluminescent teal (`0.35, 0.88, 0.80`).
   - *Act III (Rooms 7–9):* The Warden's Sanctum — dark obsidian stone, arcane amethyst flame (`0.85, 0.45, 1.0`).

## Fake doors, stacked shelves, and the stone block through the floor

Reported from play with photographic proof: *"una porta dietro le piattaforme, piattaforme mooolto vicine tra loro, compenetrazione"*.

Three stacked geometry defects revealed in room 1:

1. **A fake closed door on the backdrop wall.** In `DungeonRoomBuilder.cs`,
   `(i % 4) switch { 1 => "wall_arched", 3 => "wall_doorway", _ => "wall" }`
   placed `wall_doorway.gltf` with a closed red wooden door every 16 metres along
   the backdrop, sliced in half whenever a platform ran in front of it.
   *Fix:* Replaced `3 => "wall_doorway"` with `3 => "wall_arched"` in both `_Ready`
   and `BuildBackdrop`. Doors belong exclusively to level portals.

2. **Platform compenetration in Chimney.** `MicroChunk.cs` emitted `ledge = 1.8f`
   and stepped `_cursorX += 2.0f`. But `PlacePlatform` rounds every ledge to the
   4-metre grid: `tiles = Mathf.Max(1, Mathf.CeilToInt(r.Width / Grid))`. Every
   ledge instantiated a **4.0-metre tile** and a 4.0-metre box collider! Advancing
   by only 2.0m caused consecutive ledges to overlap horizontally by 2.0m (50%),
   turning the staircase into a cramped stack of shelves.
   *Fix:* Sized chimney ledges to the grid contract: `Emit(4f)` for each step,
   advancing `_cursorX` by 4.0m with `step = Mathf.Clamp(_m.SafeStepUp * intensity, 1.3f, 1.8f)`.
   Horizontal overlap is exactly **0.0 metres**, creating a clean, terraced metroidvania
   staircase.

3. **A stone block protruding 0.8m through the walking surface.** For elevated ledges,
   `PlacePlatform` placed `floor_foundation_allsides` at `y = at.Y - LedgeThickness * 0.5f`.
   The model `floor_foundation_allsides.gltf` has its origin at its **base** ($Y = 0$)
   and is 2.0m tall. Placed at $-0.27\text{m}$, it thrust $0.82\text{m}$ into the air,
   sitting directly in the player's path and stabbing through the underside of any
   platform above it.
   *Fix:* Removed `floor_foundation_allsides`. Capped the underside with `floor_tile_large`
   rotated 180° around X (`new Basis(Vector3.Right, Mathf.Pi)`) at $Y = -0.16\text{m}$.
   The walking surface is perfectly flat, and the underside renders as finished stone.

4. **Bot stalling on forward probes.** In `TraversalBotTest.cs`, `ledgeAbove` probed
   `pos + Vector3(0.8f, 0.9f, 0f)`. At a step-up, the probe hit the raised platform
   ahead and flagged it as an "overhead ledge", triggering `wantClimbStraightUp` which
   released `move_right`. The bot jumped in place forever.
   *Fix:* Restricted `ledgeAbove` to directly overhead (`pos + Vector3(0f, 0.9f, 0f)`).
   All 8 chunks at full difficulty now pass cleanly (Check 38: 8 of 8 PASS).


## A check that was a coin flip, and the generator underneath it

Check 60 went red in a full gate run and passed eight times standing alone on
the same build. Nothing had changed between them.

It listened for six wall-clock seconds and asserted at least three distinct
pitches. Six seconds is about **five notes**, and the figure is a random walk
stepping -3..+3 degrees, so "three distinct out of five" was a throw of the
dice. `AudioManager` already said so in a comment -- an earlier fix had made
the walk reflect at the ends rather than clamp, "because at least three pitches
was a coin flip" -- and that fix lowered the odds without removing them.

**Two diagnoses came before the right one, and both were measured rather than
argued.**

| Reading | How it died |
|---|---|
| the gate loads the machine, so fewer frames run | twelve busy loops gave the same corridor count as an idle machine |
| the synthesiser is unseeded | seeded it with the seam check 22 uses: 4, 6, 4, 5, 5 -- still drifting |

The seed failing is what pointed at the real cause. `AudioManager` held **one**
`Random`, and the melody was last in the queue for it: the ambience drips draw
from it, and so does the noise term in every jump, hit, land and dash. Those are
scheduled on `delta` and on what the player does, while the notes advance on
audio frames PUSHED. Two clocks, one generator -- so how many draws fell between
two notes depended on the wall clock and on the fight, and a busy second of
combat quietly rewrote the tune.

**Both halves are fixed, and each was proven by breaking it.**

The check now counts **notes, not seconds**: twelve of them cannot come up under
three distinct pitches unless the walk itself is broken, which is the claim being
made. A wall-clock cap remains, but only as a backstop that reports a dead
stream as one instead of hanging. Freezing the walk (`_rng.Next(-3, 4)` -> `0`)
drops it to one distinct pitch and the check fails; starving the note clock
fails it with "only 0 notes in 40s -- the music stopped".

The melody now draws from its own `Random`. `SetRandomSeedForTest` sets both,
offset by one so the streams are independent rather than identical -- seeding
only the effects generator would leave the tune drifting, which is the defect.
Measured across five runs each, distinct pitches in the corridor: **4, 5, 6, 6,
8 shared** against **6, 7, 6, 7, 5 separated**. The spread halves and the floor
rises. Ten runs is not a statistical claim and is not offered as one; it is
consistent with the mechanism, and the mechanism is the argument.

The lesson is the one the suite keeps re-teaching from a new angle. A threshold
is only as good as the sample it sits on, and "three distinct pitches" sounded
like a property of the music when it was a property of five draws. The tell was
there to be read: a check that fails in the gate and passes alone is never about
the gate.

## The boss music that outlived the boss

Found while wiring recorded atmosphere to the acts, and not by looking for it.

The beds are chosen by position in the run, with the boss theme overriding the
act while the Warden lives -- the boss room sits inside act III, so entering it
must not pull the atmosphere back off the fight. That half worked. The reverse
did not, and nothing had ever asked about it: **a room rebuild FREES the Warden
rather than killing it**, which is exactly what dying to the boss and respawning
at a checkpoint does. `Die()` announced `alive=false`; being freed announced
nothing. So `alive` stayed true with no boss anywhere in the world, and the boss
theme -- and the HUD's boss bar -- played on three rooms back.

`Warden._ExitTree` now says it is leaving.

**The check missed it first, and that is the part worth writing down.** The bed
check walked the run forwards: room 0 to the boss room, asserting each act's
bed. Silencing `_ExitTree` on purpose left it green, because a forward walk ends
at the boss and never asks what happens after. The check now walks BACK to room
0 afterwards, which is the real scenario, and the same mutation turns it red
with `boss_theme` sounding in room 0.

A fix whose mutation does not fail the suite is not covered; it is only present.

## Two clocks, one generator, and a check that was a coin flip

Check 60 went red in a full gate run and passed eight times standing alone on
the same build. Three readings, and the first two were wrong:

| Reading | How it died |
|---|---|
| the gate loads the machine | twelve busy loops gave the same count as an idle machine |
| the synthesiser is unseeded | seeded with the seam check 22 uses: 4, 6, 4, 5, 5 -- still drifting |

The seed failing is what pointed at the cause. `AudioManager` held **one**
`Random` and the melody was last in the queue for it: the ambience drips draw
from it, and so does the noise term in every jump, hit, land and dash. Those are
scheduled on `delta` and on what the player does, while notes advance on audio
frames PUSHED. So a busy second of combat quietly rewrote the tune. The melody
now has its own generator.

That was not the whole of it. The check also counted its window in wall-clock
seconds -- about five notes -- and asserted three distinct pitches, which is a
throw of the dice. Counting the window in NOTES was still not enough, and the
next gate run said so: the pitches were gathered by polling `MusicNoteHz` once
per frame, and `TickMusic` fills the whole available buffer in one call, up to
2.2 seconds and three notes, keeping only the last. On an idle machine every
note is seen; inside a gate that has just exported a 197 MB binary they are not.

`OnMusicNoteForTest` fires once per note, the same shape as the `OnBufferForTest`
hook check 22 has used since a sound pair drifted under its threshold one run in
six. Under 24 busy loops the phase now stretches from 13.3s to 19.4s -- the load
bites -- and the pitch count stays at 8.

Three lessons, and the middle one is the general one: a threshold is only as
good as the sample it sits on; an instrument that cannot see every event will
report the ones it caught as though they were all of them; and a mutation that
does not move a number is telling you something about the pairing, not about the
mechanic.

## The parry that had no arm, the arrow that was a box, and the legs that stopped

Three reports from play, and each one turned out to be a different kind of gap.

**"The sword glows slightly but it is not a parry."** The hit-stop was already
there -- 120ms on a perfect parry, 50 on a block -- and `PerfectParryMs` was
already 220, which makes the window reachable against every enemy. What was
missing was the pose. There had been an attempt at one: two `PoseBone` calls
nudging `upperarm.r` and `lowerarm.r`. They never showed, because the
AnimationPlayer was driving those same bones from `Idle_A` or `Running_A` every
frame. A single bone pushed against a full-body clip loses.

`Parry_A` is authored in `tools/anim/build_slash.py` beside `Slash_A`, and it
was looked at before it was believed: the first version brought the hand only to
mid-chest and did not read as a guard at all. At `upperarm.r` X=66 with the
elbow at 120 the fist reaches the chin with the forearm folded in front, which
is a silhouette. The guard is up by frame 3 of 22 and then HOLDS -- the window
is 380ms, and a pose still arriving when it closes is never seen.

**"The archers fire beams of light."** A fair description: the bolt was a
0.5x0.1x0.1 `BoxMesh`, albedo orange, emission 2.5. The pack ships
`arrow_bow.gltf`. Two things had to be right and neither was guessed. The model
is 1.26m down its own Z, so +90 degrees about Y lays it on the travel axis; and
which end leads was read out of the vertex buffer rather than eyeballed -- the
Z- end carries 48 vertices spread 0.163 wide, the Z+ end 25 spread 0.108, so the
bushy end is the fletching and the tip is +Z. It mirrors with the shot, because
an arrow flying left tail-first is worse than the box.

**"Hit something while moving and the character slides."** The diagnosis that
mattered came from a throwaway probe printing track paths: `Slash_A` has FIVE
tracks -- chest, both upper arms, both forearms -- and touches no leg. The legs
were not animated wrongly; they were not animated at all, because
`AnimationPlayer` plays one clip and starting the swing stopped `Running_A`.

The upper body is layered now, through an `AnimationTree` whose `OneShot` filter
is built **from the action clip's own track list**. A hand-written list of five
bone paths would be a second copy of what the clip already states, and it would
go stale the first time the rig changes.

One thing cost a red check on the way: `AnimationNodeAnimation` exposes its clip
as a resource property, not as a blend parameter, so `parameters/loco/animation`
wrote into nothing and the tree played silence.

**The check for it needed three attempts, and the failures are the interesting
part.** An absolute floor of 0.001 rad/frame passed on a body wholly replaced by
the attack clip, which still measured 0.023. A ratio against legs running free
passed too, and read 289% -- because that mutation collapses BOTH halves of the
ratio, so the control controlled for nothing. A control only controls for what
it does not share with the thing it is measuring. The bound is an absolute 0.05
rad/frame, between a measured run cycle at 0.104 and a measured collapse at
0.023, and it states what the mechanic is for rather than agreeing with itself.

## The second half had nothing new in it, and the fix exposed three checks

The complaint from play was that the level design needed review. The measurement
behind it: nine chunk kinds, all of them static distances sized from
`PlayerMetrics`; three layouts dealt by `index % 3`, so room 6 IS room 0; the
last ability at room 5 and the last gate in `Allowed` at room 4; and difficulty a
single scalar, `0.5 + 0.07 * index`. After room 5 the game had nothing left to
say and could only repeat itself louder.

Two kinds were added that ask for two verbs at once -- `Chasm` dashes across a
gap into a wall climb, `Rift` drops straight into a gap with no runway -- and
the layouts are now dealt from a bag, with the back half drawing from three of
its own built on those kinds.

**Then the crystals went entirely**, which is a design call rather than a fix:
without backtracking a gate is not a locked door you come back to. What it
bought in practice was a class of bug, and the bug arrived on schedule.

**The bug: a room's crossability depended on which layout came before it.**
Dealing from a bag put early layout 0 at room 4, and layout 0 had never run
there. Under `index % 3` it appeared at rooms 0, 3, 6 and 9 -- and at 6 and 9 the
player already held WallJump from a layout-1 room. At room 4 it did not, and the
traversal bot ran off the end of the world at 101% of the room, dying and
respawning in a loop. It crossed the same layout at difficulty 0.71 with zero
wall jumps and could not recover at 0.78. Layout 0 carries its own shaft now: a
layout whose crossability depends on what a DIFFERENT layout happened to grant
is not a layout, it is a coincidence.

**Three checks then failed, and none of them was about the level.**

*The chunk-clearance check was order-dependent.* `Gap` crossed alone in 101
frames and stalled for 1406 after `StepUp`, deterministically, on identical
geometry. The per-chunk setup reset the bot's own `_jumpHeld` flags but never
released the actual buttons, so a chunk that ended mid-jump left `jump` held --
and `PlayerController` fills its buffer from `IsActionJustPressed`, which an
already-held button never produces. The bot jumped once in 1400 frames. This is
the same defect the shaft climb had years of commits ago, one level up, and it
had been latent until an unrelated change shifted the build by a frame.

*The wall-slide check was measuring a short wall.* It took the FIRST wall over
three metres and placed the player 2.5m up its face. Shaft height scales with
room difficulty, and the wall it now found sat in room 0 at difficulty 0.5, so
2.5m was most of the way to the top: the check measured a free fall past a short
wall and reported "no wall slide", which was true and was not the defect it was
written to catch. It takes the tallest wall now and places the player at a
fraction of its height.

*The Continue check asserted a new run starts with no abilities.* It does not
any more, and the check would have been vacuous kept as it was. What separates
the two doors is the room and the cleared count, so that is what it asks.

The general shape is one this project keeps meeting: a check that passes is not
the same as a check that is asking about the mechanic. Two of these three had
been quietly measuring something else for as long as the thing they measured
happened to coincide with the thing they meant.

## The first thing in the level that moves

Every obstacle in this game was a static distance. A gap is 2.44 metres because
`SafeGap` says so; a step-up is 1.88; a shaft is a multiple of the double jump.
That is the Spatial Metric Contract working exactly as intended, and it is also
why the second half of a run read as the first half louder: when every obstacle
is a length, the only thing a later room can do is be longer.

`Sweep` is a flat corridor crossed by three blades on a 2.4-second cycle, each a
third of a cycle behind the one before. The floor is solid the whole way. What
costs you is WHEN you enter, which is a question no amount of jump tuning
answers and which survives the player owning every ability from the first frame.

**It damages rather than blocks, and that is a decision about the harness as
much as about the game.** The obvious moving obstacle is a platform you ride,
and a platform you ride is a rhythm the traversal bot cannot prove: it holds
right and never waits. Teaching it to wait would make it a better player and a
worse witness -- the whole contract rests on a deliberately clumsy bot crossing
rooms, because a room a bad bot can cross is one the metrics really do
guarantee. A blade lets a bad player through with a bruise and a good one
through clean, so the chunk stays provably crossable while the timing is real. A
ridden platform is still open, and it needs the bot to learn to wait first.

The blade eases at each end -- a cosine rather than a saw -- because a
constant-speed blade gives no cue about when it is about to turn around, and the
cue is the mechanic.

**The check for it asks whether it MOVES, not whether it exists.** A parked
blade is scenery, and scenery is precisely what the level already had; a check
that found the node would have passed on one that never budged. It measures the
spread of the blade's X across the run, against an absolute 4 metres of a
nominal 6-metre stroke -- not against the node's own `Reach`, because a
threshold read off the value under test passes whatever that value becomes.
Freezing the sweep turns it red at 0.00m.

It also cost a smaller lesson. The first version looked for both traps in
whatever room it happened to be standing in, and reported a blade travel of zero
-- correctly, since spikes open the run and blades belong to the second half,
and no room had both. Measuring two things in one place only works while they
live there.

## The finale was teaching its own mechanic

The parry is this game's signature move, and armour is the only thing that
requires it: the Warden turns every blow into 2 chip damage and opens only to a
perfect parry. The player met that demand exactly once -- in the last room,
twelve minutes in. The mechanic the whole finale rests on was being taught by
the finale.

The `Sentinel` stands at the halfway room: a `Warden` by behaviour and half of
one by numbers. It keeps the armour, the stagger and the long tell, which are
the lesson; it gives up the health, the second phase and the reach, which are
what make the Warden a wall rather than a teacher. Its stat block is a
`protected virtual Configure()` extracted from `Warden._Ready`, so it inherits
the FIGHT without inheriting the numbers -- and it spawns through the same path,
joins the same "boss" group and seals its exit the same way, because a second
parallel way of being a boss is the kind of thing this project has deleted
twice.

It is also the run's only other punctuation. Twelve minutes with one event is a
long flat line.

**Two checks went red, and one of them was the game behaving correctly.**

*The bed check* asserted act II's atmosphere at the halfway room. The halfway
room now holds a sealed armoured fight, so it takes the boss theme -- which is
what the design wants: the music marks the punctuation. The check reads act II
one room later and asserts the boss theme where it now belongs.

*The enemy-mix check* reported the second half as LIGHTER than the first, at 2.5
enemies against 5.5. Two causes stacked. The halfway room holds one enemy by
design, like the boss room, and was being counted; and the check walks the run by
teleporting onto exits, so the new seal parked it at room 5 and it surveyed six
rooms instead of nine, leaving the back half with a single sample. It now skips
both sealed fights when averaging and frees the sealed one to walk past it --
whether a door holds is check 60's question, and answering it twice in two places
is how a suite ends up with two truths about one mechanic.

The measured run is now 5, 8, 5, 4, [1], 4, 13, 4, [1]: 5.5 enemies a room in the
first half against 7.0 in the second, with the two sealed fights bracketed out.
