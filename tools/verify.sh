#!/usr/bin/env bash
# Full verification gate. Run from the repository root:
#   bash tools/verify.sh
# Exits non-zero if anything fails, so it can gate a commit or a CI step.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GODOT="$ROOT/tools/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe"
GAME="$ROOT/game"
FAILED=0

pass() { printf '  \033[32mPASS\033[0m  %s\n' "$1"; }
fail() { printf '  \033[31mFAIL\033[0m  %s\n' "$1"; FAILED=1; }

step() { printf '\n\033[1m%s\033[0m\n' "$1"; }

step "1/74  Build"
BUILD="$(cd "$GAME" && dotnet build 2>&1)"
if grep -qE "^\s+Errori: 0|^\s+Error\(s\): 0" <<<"$BUILD" || ! grep -qE "error CS" <<<"$BUILD"; then
  WARN="$(grep -oE '(Avvisi|Warning\(s\)): [0-9]+' <<<"$BUILD" | head -1)"
  pass "compiles cleanly (${WARN:-0 warnings})"
else
  fail "compile errors:"; grep -E "error CS" <<<"$BUILD" | head -5
  # Abort here. Every later step runs the compiled assembly, so continuing
  # would test the PREVIOUS build and report passes that mean nothing.
  printf '
[31mBuild failed - skipping the remaining checks, which would
'
  printf 'otherwise run against the last good assembly and lie.[0m
'
  exit 1
fi

step "2/74  Boot"
# Plain --headless is correct here: this only checks that the project loads and
# autoloads run, with no rendering involved.
# Two runs, not one: the configured boot scene is now the title, so a single
# no-argument run stopped covering the game scene the moment the title landed.
BOOT="$(timeout 60 "$GODOT" --headless --path "$GAME" --quit-after 3 2>&1)"
BOOT="$BOOT
$(timeout 60 "$GODOT" --headless --path "$GAME" scenes/Main.tscn --quit-after 3 2>&1)"
if grep -qE "ERROR|SCRIPT ERROR" <<<"$BOOT"; then
  fail "errors on boot:"; grep -E "ERROR|SCRIPT ERROR" <<<"$BOOT" | head -3
else
  pass "title and game scene both boot with no errors"
fi

# Feeds check 32. Kept as a function so the checks that do not go through
# run_scene_test (3, 4, 5) close the same channel; the defect that motivated
# check 32 happened to land in the scene suite, but nothing says the next one
# will.
ENGINE_ERRORS=""
collect_errors() {
  local name="$1" out="$2" errs
  errs="$(grep -E "^ERROR:|^SCRIPT ERROR:" <<<"$out" | grep -vc "resources still in use at exit")"
  [ "$errs" -gt 0 ] || return 0
  ENGINE_ERRORS="${ENGINE_ERRORS}${name} (${errs}): $(grep -E "^ERROR:|^SCRIPT ERROR:" <<<"$out" | grep -v "resources still in use at exit" | head -1)
"
  # Keep the evidence. This check fired on 2 of 3 gate runs and then went quiet
  # for 4 more, and 20 solo runs of the newest tests produced nothing -- an
  # intermittent that cannot be reproduced on demand has to be caught where it
  # happens, not hunted afterwards.
  {
    printf '
===== %s =====
' "$name"
    printf '%s
' "$out" | grep -B 3 -A 12 -E "^ERROR:|^SCRIPT ERROR:" | grep -v "resources still in use at exit"
  } >> "$ROOT/tools/.engine-errors.log"
}

step "3/74  Save round-trip and respawn"
rm -f "$APPDATA/Godot/app_userdata/LostCrownlike/savegame.tres" 2>/dev/null
OUT="$(timeout 90 "$GODOT" --headless --path "$GAME" scenes/RespawnTestScene.tscn 2>&1)"
collect_errors "RespawnTestScene" "$OUT"
grep -q "RESULT: PASS" <<<"$OUT" && pass "position persists to disk and is restored" \
                                || { fail "save/respawn"; grep -E "\[RT\]" <<<"$OUT" | tail -3; }

step "4/74  Combat lands its hits"
# Windowed on purpose: --headless has no renderer, so the capture harness this
# uses cannot grab frames there. See README.
rm -rf "$GAME/docs/critic-captures/_verify"
OUT="$(timeout 120 "$GODOT" --path "$GAME" scenes/CaptureRunner.tscn -- \
        --target=scenes/CombatTestScene.tscn --out=docs/critic-captures/_verify \
        --frames=160 --shot-every=160 \
        --script=10:attack_light,40:attack_light,70:attack_heavy 2>&1)"
collect_errors "CombatTestScene" "$OUT"
HITS="$(grep -c "hit Basic" <<<"$OUT")"
[ "$HITS" -eq 3 ] && pass "3 of 3 scripted attacks connect" \
                  || fail "only $HITS of 3 attacks connected"
rm -rf "$GAME/docs/critic-captures/_verify"

# Gameplay invariants. Each runs its own scene that instances the real game, so
# these check the assembled product rather than an isolated subsystem.
run_scene_test() {
  local label="$1" scene="$2" tag="$3" pass_msg="$4" extra="${5:-}"
  rm -f "$APPDATA/Godot/app_userdata/LostCrownlike/savegame.tres" 2>/dev/null
  local out
  # shellcheck disable=SC2086
  out="$(timeout 180 "$GODOT" --headless $extra --path "$GAME" "$scene" 2>&1)"
  collect_errors "$(basename "$scene" .tscn)" "$out"
  if grep -q "RESULT: PASS" <<<"$out"; then
    pass "$pass_msg"
  else
    fail "$label"
    grep -F "[$tag]" <<<"$out" | tail -3
  fi
}

step "5/74  Full game loop"
run_scene_test "checkpoint -> death -> respawn" scenes/tests/GameLoop.tscn LOOP \
  "player respawns at the checkpoint that was current when it died"

step "6/74  Every ability from the first frame"
# Inverted, along with the design. The crystals are gone: without backtracking a
# gate is not a locked door you return to, it is a chunk quietly downgraded to an
# easier one until the pickup appears -- and what it bought in practice was a
# class of bug where a room's crossability depended on which layout happened to
# be dealt before it. So this asks the question this project keeps asking in the
# other direction: is everything REMOVED actually gone? A spawner still firing in
# one late layout would be invisible, granting what the player already owns and
# reading as scenery, which is how a dead system survived here twice.
run_scene_test "all abilities, no pickups" scenes/tests/Gating.tscn GATING   "the player starts with every ability and no room hands one out" "--fixed-fps 60"

step "7/74  Enemies respect ledges"
run_scene_test "enemies stay on their platforms" scenes/tests/Ledge.tscn LEDGE \
  "no enemy fell into a generated gap"

step "8/74  Enemies can hurt the player"
run_scene_test "enemy lands a hit" scenes/tests/EnemyDamage.tscn ENEMYDMG \
  "combat is bidirectional, not one-sided"

step "9/74  Death with no checkpoint banked"
run_scene_test "recovers with no checkpoint" scenes/tests/NoCheckpointDeath.tscn NOCP \
  "dying before any checkpoint does not softlock the run"

step "10/74  Corrupt save file"
# A save can be truncated by a crash or a full disk. The game must start, not
# refuse to boot, and must not destroy the damaged file silently.
SAVE_DIR="$APPDATA/Godot/app_userdata/LostCrownlike"
mkdir -p "$SAVE_DIR" 2>/dev/null
printf 'not a valid resource\n{{{\n' > "$SAVE_DIR/savegame.tres"
OUT="$(timeout 120 "$GODOT" --headless --path "$GAME" scenes/tests/GameLoop.tscn 2>&1)"
if grep -q "RESULT: PASS" <<<"$OUT"; then
  pass "boots and plays through a corrupt save instead of refusing to start"
else
  fail "a corrupt save breaks the game"
  grep -F "[LOOP]" <<<"$OUT" | tail -2
fi
rm -f "$SAVE_DIR/savegame.tres" 2>/dev/null

step "11/74  Pause actually pauses"
run_scene_test "pause stops and resumes the world" scenes/tests/Pause.tscn PAUSE \
  "the world stops while paused and resumes after"

step "12/74  Progression survives a room change"
run_scene_test "abilities and health cross the door" scenes/tests/Progression.tscn PROG \
  "abilities, health and save survive the room rebuild"

step "13/74  No node leak across many room changes"
# A leak here would only show over a long session; 12 rebuilds in one run makes
# it visible in seconds.
run_scene_test "repeated rebuilds do not accumulate nodes" scenes/tests/RoomChurn.tscn CHURN \
  "node count stays stable across 12 room rebuilds"

step "14/74  Wall jump is real content"
# It was implemented, flagged and shown in the HUD while nothing granted it and
# no surface in the game could be slid on.
run_scene_test "wall jump is granted and usable" scenes/tests/WallJump.tscn WALLJUMP \
  "a room grants WallJump and provides walls to use it on"

step "15/74  Charge attack is a mechanic, not a flag"
# It shipped as an AbilityFlags value with a HUD icon and no behaviour at all.
run_scene_test "held heavy hits harder than a tap" scenes/tests/ChargeAttack.tscn CHARGE \
  "holding heavy deals more damage than tapping it" "--fixed-fps 60"

step "16/74  Environmental hazards exist"
# PhysicsLayers.Hazard was declared from the start with nothing ever on it, so
# the whole category of environmental danger lived only in an enum.
# Extended when the level got its first MOVING obstacle. Everything before it was
# a static distance sized from PlayerMetrics -- a gap is 2.44m, a step is 1.88 --
# so the only axis the generator had for a later room was making those numbers
# bigger, which is why the second half read as the first half louder. A blade
# cannot be sized away, and it cannot be trivialised by owning every ability,
# which matters now that the player owns them from the first frame.
#
# The claim is that it MOVES: a parked blade is scenery, and scenery is what the
# level already had. It is a pendulum, so the measurement follows the HEAD and
# not the node -- the node is the pivot and never moves, and reading it reported
# a swinging axe as a parked one. Measured as the spread of that head's X across
# the run, against an absolute 4m of a 4.5m arc rather than against the hazard's
# own ArcDegrees: a threshold read off the value under test passes whatever that
# value becomes. Freezing the swing turns this red at 0.00m.
#
# The two traps do not share a room: spikes open the run, blades belong to the
# second half. They are measured in sequence, and the builder is asked where
# each one lives rather than assumed.
run_scene_test "traps damage, and one of them moves" scenes/tests/Hazard.tscn HAZARD \
  "a spike trap damages on a repeating cooldown, and a blade sweeps its corridor"

step "17/74  Save survives a long session"
# Every death and checkpoint rewrites the file. This compresses 20 room
# changes, 20 deaths and 21 writes into a few seconds and checks the result on
# disk still matches memory.
run_scene_test "save stays coherent over many writes" scenes/tests/LongSession.tscn LONG \
  "save still loadable and matching after 20 rooms and 20 deaths"

step "18/74  State machine survives input mashing"
# 1500 frames of random press/release on every action, with all abilities
# granted so the fuzz can actually reach the airborne and dash states.
run_scene_test "no degenerate state under mashing" scenes/tests/InputMash.tscn MASH \
  "no stuck state, impossible health, NaN position or runaway speed" "--fixed-fps 60"

step "19/74  Wall slide, the state mashing cannot reach"
# Needs airborne + touching a wall + holding INTO it + NOT jumping, which random
# input breaks within a frame or two. Deliberate rather than fuzzed.
run_scene_test "wall slide clamps the fall" scenes/tests/WallSlide.tscn SLIDE \
  "the state is entered and the fall is clamped, not just falling beside a wall"

step "20/74  Combo window escalates and lapses"
# Both directions. A combo that never resets is not a combo, and it would look
# correct in any single fight.
run_scene_test "combo escalates then resets" scenes/tests/ComboWindow.tscn COMBO \
  "chaining raises damage; letting the window lapse drops it back to base"

step "21/74  Sounds are actually distinguishable"
# Nobody can listen here, so each generated buffer is characterised by length,
# spectral centroid and noisiness, and every pair must differ on at least one.
run_scene_test "no two sounds are perceptually identical" scenes/tests/SoundSeparation.tscn SOUND \
  "all 7 sounds differ in length, brightness or noisiness"

step "22/74  Falling out of the level kills and recovers"
# Found by mutation testing: disabling FallDeathY left every other check green,
# because the loop test forces death with direct damage and never falls.
run_scene_test "fall death works" scenes/tests/FallDeath.tscn FALL \
  "falling into empty space kills the player and the run resumes"

step "23/74  Coyote time"
# Found by mutation testing: zeroing CoyoteTimeWindow broke no check at all.
# Pure game feel, and it had no coverage whatsoever.
run_scene_test "jump still fires just after a ledge" scenes/tests/CoyoteTime.tscn COYOTE \
  "a jump pressed inside the grace window after leaving a ledge still fires" "--fixed-fps 60"

step "24/74  Dash invulnerability"
# Found by mutation testing: zeroing DashIFrameDuration broke no check at all.
# Dashing through an attack is a defensive option; losing it silently changes
# how every fight is played.
run_scene_test "immune while dashing, vulnerable after" scenes/tests/DashIFrame.tscn IFRAME \
  "a hit landed mid-dash is ignored, the same hit later is not" "--fixed-fps 60"

step "25/74  Camera follows the player"
# Found by mutation testing: freezing the camera broke no check at all — and a
# camera that did not follow was a real defect here once, caught by a human
# looking at screenshots.
run_scene_test "camera tracks and frames the player" scenes/tests/CameraFollow.tscn CAMERA \
  "the camera moves with the player and keeps it framed" "--fixed-fps 60"

step "26/74  Checkpoints do not re-announce"
# Also uncovered. It matters twice: the HUD prompt sticks on screen, and Save
# rewrites the file to disk on every re-entry.
run_scene_test "re-entering a checkpoint is quiet" scenes/tests/CheckpointDedup.tscn DEDUP \
  "leaving and re-entering the same checkpoint announces it once" "--fixed-fps 60"

step "27/74  Enemies that fall out are removed"
# Uncovered until mutation testing: a chasing enemy follows the player off a
# ledge by design, so without this every pursuit into a pit leaves a live body
# falling forever under the map.
run_scene_test "fallen enemies are cleaned up" scenes/tests/EnemyFallDeath.tscn ENEMYFALL \
  "an enemy dropped into the void is removed, not left falling" "--fixed-fps 60"

step "28/74  Nothing leaves the movement plane"
# The premise the whole game rests on, and mutation testing found it entirely
# unverified. Note it guards the PROPERTY, not one mechanism: the engine axis
# lock and the manual clamp are independent defences, and removing either alone
# correctly changes nothing.
run_scene_test "player and enemies stay on X/Y" scenes/tests/PlaneLock.tscn PLANE \
  "under movement, jumps, dashes and attacks, nothing drifts off the plane" "--fixed-fps 60"

step "29/74  The run has an ending"
# The three room layouts cycled on index % 3 forever, so there was no last
# room and no win state. End to end on purpose: the failures worth catching are
# that the run never terminates, terminates twice, or keeps rebuilding
# underneath the ending -- none of which a unit test of the comparison would
# see. Verified by restoring the endless cycle, which drove RoomIndex to 8 and
# turned the check red.
run_scene_test "the run ends and is recorded" scenes/tests/RunArc.tscn ARC   "six rooms, one ending, and the save records it" "--fixed-fps 60"

step "30/74  Ranged enemies"
# The second enemy type, and the first that cannot be answered by walking up
# and trading hits. Three claims, because they fail separately: the sentry is
# actually placed by the generator, its bolt damages at range, and a bolt that
# MISSES still frees itself. The third had to be added twice -- the first
# version only ever watched a bolt that hit the player, which despawns on
# impact, so deleting the timeout entirely left the check green.
run_scene_test "ranged enemy and its projectile" scenes/tests/Sentry.tscn SENTRY   "sentries are placed, hurt at range, and leave no bolts behind" "--fixed-fps 60"

step "31/74  Enemies can be told apart by behaviour"
# EnemyController's steering was private, so a subclass could close or stand
# still but never back away -- CrossbowSentry had to be written immobile for
# that reason. The ChaseSteering hook fixes it, and Skirmisher is its consumer:
# an unused extension point is the same dead parallel system this project
# already carries one of. Measured against a BasicMelee control, because "the
# skirmisher moved" is not the claim -- "it moves differently from the default"
# is. Player knockback is excluded by measuring travel away from the player's
# position AT the strike, not the live distance between the two.
run_scene_test "enemies behave differently from each other" scenes/tests/Skirmisher.tscn SKIRM   "one type retreats after striking, one holds a stand-off, and the default closes" "--fixed-fps 60"

step "32/74  The three enemy types across a whole run"
# Each type has its own check and each passes alone; that is a different claim
# from this one. The type cycle is a property of the RUN, and it has been wrong
# twice in ways no single-room test could see -- shifted by the skipped spawn
# gauntlet, then restarting per room so the third type appeared nowhere at all
# (measured: zero skirmishers in room 1). This also tears a room down with a
# bolt in the air, which is the integration risk the per-type checks cannot
# reach. Its first version reported "peak bolts in flight: 0" and so proved
# only that zero bolts had been cleaned up; it now parks the player where a
# sentry can actually see it.
run_scene_test "three enemy types across a run" scenes/tests/EnemyMix.tscn MIX   "every room populated, all three types appear, and a live bolt survives nothing" "--fixed-fps 60"

step "33/74  Enemies are a threat, and the opening is survivable"
# A two-sided bound on a player who never hits back: enemies that cannot hurt a
# stationary player are decoration, and an opening room that kills one in four
# seconds is not an opening room. It runs the exposure twice at the same
# coordinates, enemies on then off, and attributes the difference -- the first
# version measured total damage and would have called spike traps a roster.
# It also closes a real gap: check 9 proves A enemy can damage the player, and
# a sentry or skirmisher tuned to zero would have gone unnoticed.
run_scene_test "enemies threaten a passive player" scenes/tests/ThreatBudget.tscn THREAT   "damage attributable to enemies is real, and room 0 does not kill a passive player" "--fixed-fps 60"

step "34/74  The whole run can actually be played"
# Every other check in this suite teleports the player. That makes them honest
# about what they test and silent about the one thing a platformer has to get
# right: the Spatial Metric Contract claims every chunk is sized for the
# player's abilities, and until this nothing had pressed a button to find out.
#
# ALL SIX ROOMS, which is the whole arc -- three layouts, then those same three
# at saturated difficulty. It asserted only three for a while, because the bot
# could not finish the hard half and asserting more would have encoded how good
# the bot is rather than whether the game is playable. Two changes closed that:
# the chimney became a rightward staircase instead of a stack of shelves (a
# level fix, reported from play), and the bot stopped dashing into the void.
#
# Passes on the real RoomExit trigger, not a coordinate comparison, and is
# deterministic to the frame across three runs. Verified by withholding the
# WallJump grant: room 0 still crosses because it does not need it, and the
# rooms that do, fail.
run_scene_test "a bot plays the whole run" scenes/tests/FullRunBot.tscn BOT   "every room of a full run is traversable with the abilities it grants" "--fixed-fps 60"

step "35/74  Wall jumps actually climb"
# Split out from the bot on purpose: a bot that cannot climb proves nothing
# about whether a shaft is climbable. This builds two walls at exactly the
# dimensions MicroChunk computes and drives the controller's own stated
# preconditions -- press into the wall, wait until falling. It is what showed
# the shaft was fine and the bot was not: the bot never produced a clean press
# EDGE, so its jump presses filled no buffer and it wall-SLID for 600 frames
# without a single wall jump.
#
# Run at FULL difficulty (intensity 1.0, an 8.0-unit shaft), which is what
# rooms 3 and up build -- exactly the rooms the bot cannot finish. Testing the
# easier shaft would have measured the case that already works. Result: 7 wall
# jumps for 8.23 units, so the tallest obstacle the generator can produce is
# clearable and the bot's shortfall in those rooms is its own.
run_scene_test "wall jumps gain height" scenes/tests/WallShaftClimb.tscn SHAFT   "a shaft of the size the generator builds can be climbed" "--fixed-fps 60"

step "36/74  Individual chunks at full difficulty"
# A whole room is too coarse to tell "the bot is not good enough" from "this
# chunk is unclearable", so each chunk is built alone, flanked by flat ground,
# at intensity 1.0.
#
# ALL of them now. Chimney and DashGap used to be excluded, recorded with their
# measurements, because the bot fell into the dash gap and stood at the foot of
# the chimney fighting a grunt (91 attack presses) instead of climbing. Retried
# after the per-room budget became proportional to the room: both cross, in 177
# and 219 frames. Drop is new and included from the start.
#
# One thing the numbers say that the pass does not: the bot climbs the chimney
# on WALL JUMPS, not double jumps -- 2 wall jumps, 0 double jumps, 3.8 metres of
# the 3.7 it needed. The chunk that exists to teach Double Jump can be answered
# another way. That is a design note, not a failure: the claim here is that each
# chunk is clearable with what it grants, and it is.
run_scene_test "each chunk clears at full difficulty" scenes/tests/ChunkClearance.tscn BOT   "all eight chunk kinds are clearable alone at intensity 1.0" "--fixed-fps 60"

step "37/74  Continuing animations loop"
# Reported from play, missed by every check here: after 0.80s of Running_A the
# model froze in its last pose and the character appeared to slide. Every
# KayKit GLB imports with LoopMode.None, and nothing had ever looked at an
# animation after its first cycle -- the captures in this project are single
# frames, so a clip that stopped looked exactly like one that played. Checked on
# the enemies too, since they build the same libraries from the same files, and
# in both directions: looping a one-shot like Jump_Start would be its own bug.
run_scene_test "clips that should loop, loop" scenes/tests/AnimationLoop.tscn ANIM   "continuing clips loop on player and enemies; one-shots do not" "--fixed-fps 60"

step "38/74  Finishing the run leads somewhere"
# Also reported from play. The arc shipped with an ending that was a dead end:
# the last exit emitted RunCompleted, a banner appeared, and nothing else
# happened -- no restart, no menu, no way forward. Asserts the whole loop,
# because each part failed quietly: the run ends once, jump starts a new one,
# progression is wiped, and the save on disk stops claiming the run is done.
run_scene_test "a finished run can be restarted" scenes/tests/RunRestart.tscn RESTART   "the ending is reachable and leads back to a fresh run" "--fixed-fps 60"

step "39/74  Every checkpoint stands on ground"
# From a play session in room 5 that collected all three abilities and fought
# all three enemy types, yet banked only the checkpoint at the spawn. A
# checkpoint that cannot be touched sends every death back to the start of the
# hardest room in the game, which is indistinguishable from a room that cannot
# be finished. Checked by ray rather than by playing: a checkpoint the bot
# misses might be a bot taking another route, but one with nothing under it is
# unreachable for anybody. Room 5 currently passes 5 of 5 -- the check is here
# to keep it that way, not because it found the fault.
run_scene_test "checkpoints have floor under them" scenes/tests/CheckpointReach.tscn CP   "every checkpoint in the hardest room stands on ground" "--fixed-fps 60"

step "40/74  The model is still moving seconds later"
# Check 40 asserts LoopMode is set, which is the FIX, not the symptom -- and a
# check written against the fix cannot catch the next way this breaks: a clip
# that loops but is never advanced, a paused AnimationPlayer, a state machine
# that stops calling Play.
#
# This watches the skeleton instead. It holds run for three seconds and
# compares pose change per frame in the first second against the third.
# Measured with the fix removed: 0.055 rad -> 0.00000 rad, the frozen pose a
# person spotted in seconds and forty checks did not, because every capture in
# this project is a single frame and a stopped clip photographs exactly like a
# playing one.
# Extended after "attack while running and the character slides" was reported
# from play. Slash_A has five tracks -- chest and arms -- and touches no leg;
# the legs stopped because AnimationPlayer plays ONE clip, so starting a swing
# stopped the run and left them holding a pose while the body kept travelling.
# The upper body is layered over the legs through an AnimationTree now, its
# filter built from the action clip's own track list.
#
# The leg bound took two tries to mean anything, and both failures are the same
# lesson: a floor of 0.001 rad passed on a body wholly replaced by the attack
# clip, which still measured 0.023; and a ratio against legs running free passed
# too, reading 289% while nothing was running, because that mutation collapses
# both halves of the ratio. It is an absolute 0.05 rad/frame now, between a
# measured run cycle at 0.104 and a measured collapse at 0.023.
run_scene_test "the run animation keeps advancing" scenes/tests/AnimationMotion.tscn MOTION   "the model still animates three seconds in, and the legs keep running through a swing" "--fixed-fps 60"

step "41/74  Attacks can be seen coming"
# The prerequisite for a parry, and worth having on its own. Every enemy used to
# wind up with no distinct pose: EnemyState.Attack mapped to the same Throw clip
# for all three types, and the free KayKit pack has no melee swing to map
# instead. An attack with no tell can only be answered by luck, and a
# perfect-parry window hung off an invisible wind-up would be a guess dressed as
# a mechanic.
#
# Three independent claims: the wind-up lasts long enough to react to, the arm
# actually moves, and the weapon lights up -- at this camera distance a raised
# arm on a character a few pixels tall is not a signal by itself. The glow is
# read off the weapon's own material, so it fails if the overlay stops being
# applied. Measured: 1.40s of wind-up, 60 degrees of arm, glow 3.33.
run_scene_test "attacks telegraph before they land" scenes/tests/AttackTell.tscn TELL   "the arm draws back and the weapon lights up before a blow" "--fixed-fps 60"

step "42/74  Parry, and perfect parry"
# Four claims, each able to fail alone: an unguarded blow hurts (the baseline,
# without which the rest could pass on an enemy that never connects), a guard
# raised in time stops the damage, a guard raised too late still stops it but
# does NOT punish, and only a perfect one staggers the attacker. That third one
# is the mechanic: without it "perfect" is indistinguishable from "parried".
#
# The enemy is placed in reach and left to swing on its own timing -- scripting
# the swing would test the test's idea of when a blow lands rather than the
# game's. Measured: 3 hits unguarded, 0 late, 0 perfect, stagger only on perfect.
run_scene_test "a parry turns a blow, a perfect one punishes" scenes/tests/Parry.tscn PARRY   "guarding stops the blow; only the perfect window staggers the attacker" "--fixed-fps 60"

step "43/74  The dead are cleaned up"
# Die() deliberately leaves the body in the scene so the death animation can
# play and the corpse can slide to a stop -- and nothing freed it afterwards. A
# room is not rebuilt while you are fighting in it, so bodies simply piled up:
# measured at twelve kills, twelve bodies, none removed. Check 15 did not catch
# it because it counts nodes ACROSS rebuilds, and a rebuild frees everything.
run_scene_test "bodies do not pile up" scenes/tests/Corpse.tscn CORPSE   "corpses linger long enough to read, then sink and are freed" "--fixed-fps 60"

step "44/74  The voice pool recycles"
# The pool promises to spread across voices "without ever cutting off a
# still-sounding one", stealing only when a burst genuinely outruns it. That
# promise was void: AudioStreamPlayer.Playing never goes false for an
# AudioStreamGenerator -- the stream does not end, it emits silence -- so after
# eight sounds every voice looked busy and every later sound stole one. Found in
# a real playthrough: twelve sounds, five steals, seconds apart.
#
# Sounds are spaced by WALL time here, not by frames: the pool reserves a voice
# for the real duration of its sound, which is right, and a first version of
# this check spaced them 20 frames apart and called that a third of a second.
# Under --fixed-fps in headless a frame is about a millisecond, so it measured 8
# steals and accused a working pool. Both directions are asserted -- spaced play
# steals nothing (0), a burst of 16 still does (4) -- because a pool that never
# steals is one that drops sounds.
run_scene_test "voices are reused, not stolen" scenes/tests/VoicePool.tscn VOICE   "spaced sounds never cut a voice off; a real burst still steals" "--fixed-fps 60"

step "45/74  A long run WITH A RENDERER stays quiet"
# The gap this closes is structural, and it took a bug to find it. Almost every
# check here is --headless, which has no renderer -- so the skeleton, the
# animation player and everything drawn are barely exercised. That is exactly
# the class of defect a person kept reporting and the suite kept missing.
#
# Proof it was a real gap: bone posing fed its own output back into
# Quaternion.Slerp every frame, float drift accumulated, and Slerp throws on a
# non-unit quaternion. A windowed run flooded 119 errors. The SAME bot, the SAME
# rooms, headless: zero, every time.
#
# Windowed, and long enough for drift to build: 4600 frames is the length that
# actually reproduced it. 2600 was tried first and passed WITH the bug present,
# which is the reason this number is written down rather than picked. It asserts
# nothing about the run's outcome -- check 37 does that -- only that the engine
# printed nothing.
OUT="$(timeout 300 "$GODOT" --path "$GAME" scenes/CaptureRunner.tscn --         --target=scenes/tests/FullRunBot.tscn --out=docs/critic-captures/_render         --frames=4600 --shot-every=4600 2>&1)"
collect_errors "FullRunBot(windowed)" "$OUT"
RENDER_ERRS="$(grep -cE "^ERROR:|^SCRIPT ERROR:" <<<"$OUT")"
[ "$RENDER_ERRS" -eq 0 ] && pass "4600 frames of real play with a renderer, no engine errors"                          || fail "$RENDER_ERRS engine errors in a rendered run"
rm -rf "$GAME/docs/critic-captures/_render"

step "46/74  The dungeon is not silent"
# Between hits the game made no sound at all, which reads as being switched off
# rather than as quiet. The bed is a drone pushed in chunks as its buffer
# drains, so the failure mode is not silence but "two seconds and then nothing"
# -- indistinguishable from working code in any check that is too short.
#
# Timed in WALL seconds, not frames: the generator drains its buffer in real
# time whatever the engine's step is doing, and a frame-counted first version
# saw one refill in what it believed was eleven seconds and failed a working
# bed. It also asserts the ambience takes NONE of the eight pooled voices --
# a drone holding one permanently would starve gameplay cues the same way the
# never-released-voice bug did.
run_scene_test "ambience runs without starving the pool" scenes/tests/Ambience.tscn AMB   "the bed runs continuously on its own voice, refilling as it drains" "--fixed-fps 60"

step "47/74  The pause menu is in the game"
# A PauseMenu with Resume and Quit existed for a long time and appeared only in
# a UI test scene -- it was never added to Main.tscn. So in the actual game the
# pause key froze the world and showed nothing, with no way out but pressing it
# again. That is this project's recurring failure: something that exists, is
# correct, and is connected to nothing.
#
# Run against the REAL Main.tscn for exactly that reason; a check that instanced
# the menu itself would have passed the whole time. Two details the first
# attempt got wrong: Input.ActionPress sets an action's state but produces no
# InputEvent, so a menu reading _UnhandledInput never sees it, and a test that
# pauses the tree stops processing itself unless it opts out -- it hung silently
# instead of failing.
run_scene_test "pause menu reaches the player" scenes/tests/PauseInGame.tscn PAUSEUI   "the pause menu shows, resumes cleanly, and its restart starts the run over" "--fixed-fps 60"

step "48/74  The game boots into a title"
# The scene path this check uses is read from ProjectSettings at runtime, not
# typed into the test. That is the whole check: a title screen that exists and
# looks right but is not what run/main_scene points at is invisible to the
# player, which is the same shape as the pause menu above. It also catches two
# actions bound to one key -- parry and attack_light were both on mouse-left,
# so every click did both, and listing the bindings side by side is the only
# place that shows.
run_scene_test "the title is the boot scene" scenes/tests/TitleBoot.tscn TITLE   "the game boots into a title with no world behind it, and Play starts the run" "--fixed-fps 60"

step "49/74  The last room is a boss fight"
# Not "there is a big enemy at the end": the RULE is what is checked. Armoured,
# a 55-damage heavy is reduced to 2 and does not interrupt; parried open, the
# same blow lands in full; and the exit does not work while it lives. With the
# armour removed the first measurement reads 55 and this fails, which is the
# only thing that separates the boss from a grunt with a lot of health.
run_scene_test "the boss gates the ending" scenes/tests/BossFight.tscn BOSS   "armoured until a perfect parry opens it, and the way out is behind it" "--fixed-fps 60"

step "50/74  Blows leave a mark, and absorbed ones look different"
# Headless on purpose. Not "do pixels appear" -- the windowed check covers the
# drawn layer -- but "does the spawn happen on every path that claims it", and
# "does a burst free itself". Both are counts. The colours are asserted to
# DIFFER rather than to be particular values: a check pinned to (1, 0.34, 0.26)
# would fail on a palette tweak and pass on the only change that matters, the
# two cases collapsing into one. Finding this also showed that the landing
# camera punch had never been visible: at the old decay rate a 0.08 shake was
# gone in under a frame.
run_scene_test "impacts are visible and do not linger" scenes/tests/ImpactFx.tscn FX   "a landed blow, an absorbed one and a perfect parry each read differently, and none accumulate" "--fixed-fps 60"

step "51/74  Keys can be rebound, and one key means one action"
# Reachability is asserted first and on purpose: the button is pressed on the
# real Title.tscn, because every menu in this project was correct before it was
# reachable. The rest is the rule that a key belongs to one action -- rebinding
# onto a taken key is REFUSED, not swapped, since a swap silently moves a
# binding the player was not editing. And the change is read back FROM DISK: a
# rebind can be applied and never written, and nobody finds out until the next
# launch.
run_scene_test "rebinding survives a restart" scenes/tests/Options.tscn OPT   "options open from the title, a taken key is refused, and the change is on disk" "--fixed-fps 60"

step "52/74  Enemies face what they are attacking"
# Reported as "enemies strike in the opposite direction". The mechanism is
# narrower: damage is radial and a bolt aims from the player's position, so the
# hit always landed -- what pointed the wrong way was the MODEL. Facing was
# driven by velocity, TickAttack zeroes velocity so the enemy commits in place,
# and a sentry never moves at all. Nothing in the suite could see it, because
# every assertion was about damage; a tell that points the wrong way is the one
# piece of information a parry is timed against.
run_scene_test "enemies turn to face the player" scenes/tests/EnemyFacing.tscn FACE   "a committed melee enemy turns mid-windup, and a stationary sentry turns at all" "--fixed-fps 60"

step "53/74  The floor has an underside, and no hole is dark"
# Reported as "the floor seems to vanish and the platforms look fragmented in
# the void". Two causes: a walkable surface was a 15cm plank with nothing under
# it, and the torches are at y=2.4 with nothing below, so the space to jump
# across was the darkest part of the frame. Asserted against what was BUILT --
# the first attempt used the pack's foundation piece on assumed dimensions, and
# it is 2.2 wide on a 4-unit grid with its origin at its base, so it stood 1.5m
# ABOVE the floor it was meant to be under.
run_scene_test "platforms stand on something" scenes/tests/FloorLegibility.tscn FLOOR   "every wide platform has a stone face under it and every hole has a light in it" "--fixed-fps 60"

step "54/74  An arena cannot be run past"
# The Arena chunk exists to teach the charge attack and was the easiest ground
# in the room to sprint across -- the widest flat run, enemies in the middle.
# The gate that fixes it is checked for OPENING as much as for blocking: a gate
# that never lifts is worse than no gate, and a check written only around "the
# player is blocked" would call that a pass.
run_scene_test "the arena holds until it is cleared" scenes/tests/ArenaLock.tscn ARENA   "running at the barrier gets nowhere, clearing the arena opens it" "--fixed-fps 60"

step "55/74  Enemies hold the ground they were placed on"
# Every enemy in every generated room patrolled toward world x = -3, wherever
# it stood: the builder added the enemy to the tree, THEN wrote its position,
# THEN wired its patrol markers, and _Ready runs on the first of those three.
# Survivable while a room was 64 metres; at 200 they set off across the level
# and three of eight walked out of the world in one run. Found by a corpse at
# (98.0, -93.7) in a check that was counting bodies for another reason.
run_scene_test "enemies guard their own ground" scenes/tests/EnemyPost.tscn POST   "patrol anchors sit near the enemy, and the room still has its population ten seconds later" "--fixed-fps 60"

step "56/74  The game exports and starts as a binary"
# The last thing between the project and a person who is not sitting at this
# machine. It is checked because the export FAILED SILENTLY for as long as it
# existed: the .NET side needs a LostCrownlike.sln next to the .csproj, the
# project had only ever been built with `dotnet build` on the csproj, and
# without the solution Godot still wrote an exe and a pck -- one that launched,
# showed nothing, and crashed with signal 11 in the debug build, because the
# assemblies were never published. So this asserts the DLL is there and that
# the binary starts with its C# autoloads running, not that the export command
# returned zero.
if bash "$ROOT/tools/export.sh" "$ROOT/build/verify" >/tmp/export-check.log 2>&1; then
  pass "$(tail -1 /tmp/export-check.log | tr -d '' | sed 's/\[[0-9;]*m//g')"
else
  fail "the game does not export to a runnable binary:"; tail -3 /tmp/export-check.log | tr -d '' | sed 's/^/        /'
fi
rm -rf "$ROOT/build/verify"

step "57/74  Continue continues, and a new run is new"
# A run is ten rooms and twelve minutes, so it cannot only be played in one
# sitting. Two entry points are worth having only if they differ, and each of
# them fails in a way that looks fine: a Continue that starts at room 0 is a
# second New run with another label, and a New run that keeps the save is the
# old one with the level reset -- which is what it did, because SaveManager
# re-announces the saved abilities on boot. Measured with the fix removed:
# "New run" opened with DoubleJump and Dash in hand and four rooms cleared.
run_scene_test "the title's two doors differ" scenes/tests/Continue.tscn CONT   "Continue resumes the saved room, New run starts over with nothing" "--fixed-fps 60"

step "58/74  There is music, and it moves"
# The dungeon already had a drone, which is a room tone: twelve minutes of one
# held chord is a hum rather than a score. Three claims, and the first two are
# what a "there is audio" check would miss -- the generated voice never runs
# dry (an unfed stream stops silently and stays stopped), the notes actually
# change, and the boss is not the corridor. Finding the second one working
# turned up that the figure was clamping to the bottom of its scale: six
# seconds produced two distinct pitches, which made the music dull and the
# check a coin flip.
run_scene_test "the music plays and wanders" scenes/tests/Music.tscn MUSIC   "the music voice is never starved, the figure moves, and the boss sounds different" "--fixed-fps 60"

step "59/74  Each act has its own atmosphere"
# The recorded beds. Three CC0 atmospheres and a boss theme, bound to WHERE the
# player is by a rule rather than a table -- BedForRoom divides the run into
# thirds, so the mapping survives RunLength being retuned, and RunLength has
# already moved once, from six to ten.
#
# Over the bed sits a second, quieter layer chosen by the room's SHAPE -- flat,
# open, or a climb -- classified in World against the player's own jump, so two
# rooms cut from the same layout still do not sound identical.
#
# It walks the whole run and then walks BACK, and the walk back is the half that
# found a real defect. The boss room sits inside act III, so entering it emits
# an act-III fraction; the boss override handles that. Nothing handled the
# reverse: a room rebuild FREES the Warden rather than killing it -- which is
# exactly what dying to the boss and respawning at a checkpoint does -- so
# `alive` stayed true with no boss in the world and the boss theme played on
# three rooms back. Warden._ExitTree now says it is leaving.
#
# Verified in three directions, each turning this red: dropping the boss
# override leaves act III in the boss room; collapsing the room shapes to one
# gives 1 distinct layer of 3; and silencing Warden._ExitTree leaves the boss
# theme playing in room 0.
run_scene_test "each act sounds like itself" scenes/tests/Beds.tscn BEDS   "acts, boss and all three room shapes each get their own atmosphere" "--fixed-fps 60"

step "60/74  An armoured fight at the halfway mark"
# The parry is this game's signature move, and armour is the only thing that
# REQUIRES it: every blow becomes 2 chip damage and the only thing that opens
# the fight is a perfect parry. Until the Sentinel the player met that demand
# once, in the last room, twelve minutes in -- so the mechanic the finale rests
# on was being taught by the finale. It is also the run's only punctuation.
#
# What is asserted is what makes it a lesson rather than a bigger grunt: a
# 55-damage blow lands as chip, the door does not open while it lives, and it is
# a Sentinel rather than the Warden. A check that only found "an enemy in room 5"
# would pass on a reskinned melee grunt, which is what this must not be.
# Dropping the seal turns it red.
run_scene_test "the halfway fight is sealed and armoured" scenes/tests/Sentinel.tscn SENTINEL   "an armoured fight at the halfway mark, sealed in, and not the Warden" "--fixed-fps 60"

step "61/74  A lever the player has to act on"
# The first thing in this game the player DOES to the world rather than
# crosses. Everything else is walk-into-it: a checkpoint announces itself when
# you touch it, a hazard hurts you when you touch it, an arena gate opens when
# the last enemy dies. Nothing had ever asked for a decision that was not
# "when do I jump".
#
# It is STRUCK, not pressed, and that is about the harness as much as the game:
# a use key is a verb the traversal bot does not have, and a chunk the bot
# cannot open is a chunk the metric contract cannot promise. The bot already
# swings at what blocks it.
#
# The last clause of the claim is the one that matters. The bot crossed this
# chunk with four wall jumps and one attack, which is also what getting past
# WITHOUT throwing the lever looks like -- so this drives the player at the slab
# and asserts it does not pass, strikes the lever, and asserts it then does.
# A lever that ignores damage leaves the player at the slab and turns it red.
run_scene_test "the slab holds until the lever is struck" scenes/tests/Latch.tscn LATCH   "a barrier holds, a struck lever opens it, and only then does the player pass" "--fixed-fps 60"

step "62/74  A floor that carries you"
# The moving platform, and the shape it had to take. A platform you RIDE is a
# rhythm the traversal bot cannot prove: it holds right and never waits, and a
# bot taught to wait stops being the deliberately clumsy witness the metric
# contract rests on. A moving FLOOR composes with holding right instead of
# fighting it -- it drags you back over one stretch and throws you forward over
# the next, so the same button produces two speeds.
#
# Crossability is the wrong question here: the bot crossed this chunk in 289
# frames whether or not the floor moved, because the drag is a fraction of its
# run. So the player is stood still WITH NO INPUT on each stretch and its drift
# is measured -- standing still is the only state in which the floor is the only
# thing moving you. Zeroing ConstantLinearVelocity turns this red at 0.00m.
run_scene_test "the floor carries a standing player" scenes/tests/Current.tscn CURRENT   "one stretch drags a standing player back, the next carries them forward" "--fixed-fps 60"

step "63/74  3-channel telegraph, biome forks, and persistent shortcuts"
# Verifies the 3-channel combat telegraph (unparryable red bypasses guard),
# the room 3 dual branching exits (Catacombs and Crucible), and the persistent
# world flag spawning the shortcut exit in room 0.
run_scene_test "telegraph bypass, room 3 dual exit, and shortcut portal" scenes/tests/Telegraph.tscn TELEGRAPH "3-channel telegraph, dual branch exits, persistent shortcuts verified" "--fixed-fps 60"

step "64/74  The two doors at the end of room 3 build different rooms"
# The branch existed for a whole round without this and was a lie the entire
# time: both portals called BeginRebuild(4) and produced byte-identical rooms.
# A check asserting "the second exit exists" would have stayed green throughout,
# which is why this one counts CONTENT -- layout, arena gates and roster -- from
# the same room index entered two ways.
run_scene_test "the Crucible fork builds a different room" scenes/tests/CrucibleFork.tscn FORK   "the two room-3 doors build different rooms: layout, arenas and roster all differ" "--fixed-fps 60"

step "65/74  The Forge Heat rises on schedule"
# The branch's premise is a clock the player winds back by fighting well, so a
# wrong clock is a wrong design no amount of correct combat code repairs. From
# the entry heat of 25 at room 4's 2.80/s the bar tops out at 26.8s. Measured in
# isolation and with the live-heat-source count asserted at zero, so the figure
# is attributable to the base rate rather than to an Emberwright nobody counted.
run_scene_test "a passive player flashes over on schedule" scenes/tests/HeatCurve.tscn HEATCURVE   "a passive player flashes over at the designed time, from the base rate alone" "--fixed-fps 60"

step "66/74  A perfect parry drains exactly twelve"
# The half of the curve the player controls, asserted as an EXACT figure. A
# drain of one also goes down, and would make the branch unwinnable while every
# other check stayed green. Both readings are taken inside one _Process call, so
# no passive gain accrues between them and the difference is the parry alone.
run_scene_test "parry drains are exact and differ by channel" scenes/tests/HeatDrain.tscn HEATDRAIN   "a perfect parry drains 12 and a blocked one 6" "--fixed-fps 60"

step "67/74  The heat promotes an ordinary grunt up the telegraph ladder"
# A BasicMelee has one hard-coded telegraph, so every channel it is ever seen on
# came from the room. Three landings: white when cold, GOLD at Molten (one step,
# not two -- red from a grunt is the flashover's doing) and red in a flashover.
run_scene_test "heat promotes a grunt's telegraph" scenes/tests/TelegraphPromotion.tscn PROMOTION   "a grunt's white strike becomes gold at Molten and red in a flashover" "--fixed-fps 60"

step "68/74  The Slagbound's shell chips, burns, and opens only to a perfect parry"
# Absorbing damage is the Warden's trick; hitting BACK at whoever swung is what
# makes this type worth having. A BLOCKED parry is fired first, so the check
# cannot pass on an implementation that opens for any parry at all.
run_scene_test "the shell punishes mashing and opens to a perfect parry" scenes/tests/SlagboundShell.tscn SHELL   "the shell chips, burns the hand that struck it, ignores a block and opens to a perfect parry" "--fixed-fps 60"

step "69/74  Where an Emberhusk dies is the encounter"
# Two kills, and the second is what makes the first mean anything: without the
# far-away control, "the detonation dealt damage" passes on a blast with
# infinite radius. The fire is confirmed to EXIST at mid-life before it is
# confirmed gone -- the vacuous-cleanup class this project has hit four times.
run_scene_test "the husk's blast is local and its fire clears" scenes/tests/EmberhuskBlast.tscn BLAST   "a husk killed close costs health and one killed far costs none; the fire burns then clears" "--fixed-fps 60"

step "70/74  The Forgemaster's door is the heat, not its health"
# Sealed FIRST. A check that only proved the parry works below the threshold
# would pass on a boss with no gate at all, which is the version this replaced.
run_scene_test "the heat gates the Forgemaster" scenes/tests/ForgemasterGate.tscn GATE   "at Molten nothing opens it; below, a perfect parry buys a 1.5s window" "--fixed-fps 60"

step "71/74  What the Colata actually costs, with a control run"
# Two-sided: under the floor the branch is not a branch, over the ceiling it
# kills the player for choosing it. Run twice at the same coordinates with
# SpawnEnemies on and then off, because total damage is not the roster's damage
# -- the room also has spikes, blades and a floor that burns past 70. Skipping
# that control is how a threat figure once stayed green with the grunt disarmed.
run_scene_test "the Crucible roster's damage is measured, not claimed" scenes/tests/CrucibleThreat.tscn CRUTHREAT   "the Colata's roster deals its designed damage in four seconds of standing still" "--fixed-fps 60"

step "72/74  Roguevania hub and meta-progression"
# Verifies persistent Ember collection, Rune Forge modal perks (rune_vigor,
# rune_parry_window, rune_parry_heal), Room 0 Runestone Shrine, and cross-run persistence.
run_scene_test "meta-progression currency, perks, shrine and persistence" scenes/tests/MetaProgression.tscn META \
  "embers currency, rune perks, camp shrine and persistence all verified" "--fixed-fps 60"

step "73/74  Roguevania loop audio-visual feedback and run summary"
# Verifies procedural anvil/heal audio synthesis, restorative radiance burst, and enriched run complete banner.
run_scene_test "audio cues, heal burst, and run complete summary" scenes/tests/RoguevaniaLoop.tscn ROGUELOOP \
  "procedural rune/heal audio, restorative visual burst, and run summary verified" "--fixed-fps 60"

step "74/74  The engine stays quiet"
# Cross-cutting. Each scene test above asserts its own outcome; this asserts
# that reaching that outcome printed no engine error. It is the check that was
# missing when every room transition in the shipped game failed a resource
# load and 31 green checks said nothing. Scope: every check from 3 onward.
# The single exclusion is "N resources still in use at exit" -- engine shutdown
# accounting, seen in about 1 run of 5 of ComboWindow and never reproducible
# under --verbose. Admitting it would make this check fail at random, and a
# gate that fails at random teaches you to ignore it.
if [ -z "$ENGINE_ERRORS" ]; then
  pass "no engine errors printed anywhere in the run"
else
  fail "engine errors during otherwise-passing tests:"
  printf '%s' "$ENGINE_ERRORS" | sed 's/^/        /'
fi

printf '\n'
[ "$FAILED" -eq 0 ] && printf '\033[32mAll checks passed.\033[0m\n' || printf '\033[31mSomething failed above.\033[0m\n'
exit "$FAILED"
