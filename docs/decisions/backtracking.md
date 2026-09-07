# Is backtracking still the goal?

**Open. Awaiting the Architect's opinion.** Prepared against `f08ecc9`.

`PROMPT.md` promises an interconnected world with ability-gated backtracking.
The game does not have one, the machinery for it was deleted, and the ability
gates went with it two days ago. Before spending two to three weeks rebuilding
towards that promise, it is worth asking whether it is still the right promise.

The question is not "how would we build backtracking" -- that is answerable and
costed below. It is whether a twelve-minute run that ends in a boss should
become a two-hour interconnected world.

Everything here separates **what was measured in the tree** from **what one
agent thinks about it**. Disagree with the second freely; the first is
checkable.

## Measured

- `tools/verify.sh` runs **63 checks**, all green, and every one has been shown
  to fail correctly.
- The level vocabulary is **14 chunk kinds**: Gauntlet, Gap, DashGap, Chimney,
  StepUp, Drop, Spikes, Arena, WallShaft, and five added in the last two days --
  Chasm, Rift, Sweep, Latch, Current.
- **28 commits in two days.** `docs/engineering-log.md` is 1,518 lines;
  `ARCHITECTURE.md` is 880.
- `game/src/World/WorldGraph.cs`, `game/src/Core/LevelManager.cs` and
  `game/scenes/world/` **do not exist**. They were deleted; the reasoning is in
  `ARCHITECTURE.md` under "Resolved: there is one room system".
- `SaveData` has five fields: `PlayerPosition`, `UnlockedAbilities`,
  `LastCheckpointId`, `RoomsCleared`, `RunCompleted`. **None is per-room.** A
  `WorldFlags` dictionary existed and was removed as a promise of persistence
  nothing provided.
- There is **one room node**, rebuilt in place by `RebuildAs(index)`. Room 4 and
  room 7 are the same object with different contents.
- **Ability gating was removed**, at the owner's instruction. The player holds
  all four abilities from the first frame; the crystals and their HUD icons are
  gone.

### Why the gates went, since it bears on this

Not for taste. Layouts are now dealt from a bag rather than by `index % 3`, and
that put early layout 0 at room 4 -- a room where, under the old cycle, it had
never run. It crossed at difficulty 0.71 with zero wall jumps and could not be
recovered at 0.78, because the wall jump it needed had been granted by a
*different* layout that no longer came first. The traversal bot ran off the end
of the world at 101% of the room.

> **Judgement.** A gate in a linear run is not a locked door you come back to.
> It is a chunk quietly downgraded to an easier one until a pickup appears --
> and what it bought in practice was a class of bug where a room's crossability
> depended on which layout had been dealt before it. Removing it was right *for
> the game as it stands*. It is also the single thing that moves backtracking
> furthest away, because it removed the currency.

## What backtracking would require

| Requirement | State today | Estimate |
|---|---|---|
| **A connected world.** Rooms with more than one exit, and a graph saying what joins what. | `RoomIndex + 1` is the entire topology. | 3-4 d |
| **Rooms that remember.** Keep visited rooms instanced, or persist what happened and re-apply it on rebuild. | Nothing per-room is saved. Rebuilding is the only way a room exists twice. | 3-5 d |
| **A reason to return.** Something seen early that a later thing opens. | No gates, no keys, no map. `Latch` is the nearest primitive. | 2-3 d |
| **A map.** Without one the player cannot know where to return to. | Does not exist. | 2-3 d |

> **Judgement.** The currency does not have to be an ability. `Latch` -- a
> barrier that opens when its lever is struck -- is already the metroidvania
> primitive, and it needs no gating: put the lever in one room and the barrier
> in another and "you have thrown that lever" becomes the thing the world
> remembers. Cheaper and less regressive than reinstating ability gates, and it
> survives the player owning everything.
>
> The expensive requirement is the second, and it is expensive in a specific
> way: **a world that remembers cannot be regenerated**. Procedural rooms
> rebuilt in place are what makes this project cheap to change; persistence is
> what makes them stop being cheap. That tension is the real decision, not the
> day count.

## What the same weeks buy elsewhere

The run has five ideas in its back half that it did not have three days ago. It
has never been played end to end by anyone but its author, and the one thing
sixty-three checks cannot report is whether crossing a room was any good. Two to
three weeks of tuning, encounter design and art direction is the alternative use
of the same time, and it compounds with a structure that already works.

> **Judgement, and the recommendation to argue with.** Backtracking is not item
> five on a list of level-design improvements. It is the next project: it
> replaces a twelve-minute run with a two-hour world, and it turns procedural
> generation from the thing that makes this project fast into the thing that
> fights it. The recommendation is **not now** -- and if the promise in
> `PROMPT.md` is to be dropped, drop it explicitly in that file rather than
> leaving it as a stated goal nobody is working towards.

## What your opinion would settle

**1. Is the twelve-minute run the product, or a prototype of something longer?**
Every other answer follows from this one, and it is not a technical question. If
it is the product, backtracking is scope creep with a pedigree. If it is a
prototype, the persistence work should start before more content is built on top
of a room that forgets.

**2. Is a world that remembers compatible with rooms that are generated?**
Two credible answers: persist a seed plus a delta per room and rebuild
deterministically, or stop generating and author. The first keeps the current
architecture and risks a long tail of "the room came back wrong"; the second
throws away the Spatial Metric Contract, which is the best thing in this
codebase.

**3. If backtracking happens, is the currency a lever or an ability?**
Levers cost nothing to reinstate and survive the player owning every ability.
Abilities mean reintroducing gating, removed for a measured reason two days ago.
A third option -- keys or charges as items -- has not been costed.

**4. Does `PROMPT.md` still describe the target?**
It also promised procedural art with no external asset files, and the project
now ships KayKit models and seven CC0 Vorbis tracks. That divergence is
recorded. The metroidvania promise is not, and an unmarked stale goal in the
founding document is worse than a dropped one.

---

Measured claims are checkable against the tree at `f08ecc9`; run
`bash tools/verify.sh` for the 63 checks. Reasoning for anything surprising is
in `docs/engineering-log.md`, newest entries last.
