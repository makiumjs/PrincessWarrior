# Critic round 4 — after the visual and gameplay passes

Measured from `docs/critic-captures/round4/` (330 frames, scripted) plus two
controlled performance runs. Compare against `game-round3.md`.

## Movement feel: 8/10 (was 7)

**Input latency: still 0 frames** on every scripted jump.

**Deaths in the opening run: 0, down from 2.** The difficulty ramp works — room
0 now runs its obstacles at 55% and opens with flat ground and a step instead
of a hole. Two checkpoints were banked in the same run, so the player is
actually progressing rather than dying in place.

**Performance, and a correction to my own first reading.** The raw capture
showed p99 83.27ms / max 91.53ms, far worse than round 3, and I initially took
that for a regression from the new lights and parallax. It was not. The spikes
land on the frame immediately AFTER each screenshot (56, 111, 166, 221, 276 for
`--shot-every=55`) — capture cost, not gameplay cost. My first attempt to
filter them out was too narrow, because the cost bleeds past a single frame.

Measured properly, with one screenshot at the very end of a 300-frame run:

| | p50 | p95 | p99 | max |
|---|---|---|---|---|
| torches on  | 16.67 | 16.98 | 17.47 | 17.58 |
| torches off | 16.66 | 16.97 | 17.40 | 20.05 |

The lights cost nothing measurable. The frame time is pinned flat at the 60fps
cap. Had I not run the controlled comparison I would have optimised a problem
that does not exist.

Standing caveat, unchanged: the harness pins `Engine.MaxFps = 60`, so 16.66ms
is the ceiling, not the headroom. This says the game does not hitch; it does
not say how much margin remains.

## Visual fidelity vs Lost Crown: 6/10 (was 4)

Every item on round 3's list is addressed:

- **Background depth** — two parallax layers above the play wall, dimmed with
  distance and shifted by a fraction of camera movement. (Orthographic cameras
  have no perspective divergence, so this had to be done by hand.)
- **Lighting** — mounted torches with warm `OmniLight3D` pools and per-torch
  flicker on layered sines plus noise, each with a randomised phase. The key
  light was dropped from 1.5 to 0.45 and the fill from 0.5 to 0.12: a torch can
  only read as a source if the room is not already fully lit without it.
- **Empty upper third** — filled by the backdrop layers; a second wall course
  below the floor fills the band under the player.
- **Environment animation** — verified with an empty input script: 9 of 9
  captured frames differ, where the room was previously pixel-identical.
- **Stuck checkpoint prompt** — fixed at the source. The trigger was
  re-announcing on every re-entry, and the player respawns inside its own
  checkpoint, so each death also made Save rewrite the file to disk.

What still separates it from Lost Crown, honestly: no particle effects, no
weather or dust, hand-authored art direction rather than a kit, and character
animation that is a generic run cycle rather than a character with a walk that
says something about them. These are art and direction, not code.

## Recommendation

The code-side backlog from round 3 is empty. Further movement on the visual
score needs art decisions, not engineering. The one engineering item worth
doing is the architectural drift documented in ARCHITECTURE.md — two room
systems, one unused.
