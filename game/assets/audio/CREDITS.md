# Audio credits

Every file here is **CC0** (Creative Commons Zero): copy, modify, distribute and
perform, commercially included, with no permission and no attribution required.
Credit is given anyway, as it is for the KayKit art.

Each licence was read on the sound's own Freesound page, not taken from the
search filter.

| File here | Freesound | Author | Original |
|---|---|---|---|
| `act1_forgotten_crypts.ogg` | [565841](https://freesound.org/s/565841/) | szegvari | Atmosphere dark 1 |
| `act2_sunken_catacombs.ogg` | [565840](https://freesound.org/s/565840/) | szegvari | Atmosphere dark 2 |
| `act3_wardens_sanctum.ogg` | [565839](https://freesound.org/s/565839/) | szegvari | Atmosphere dark 3 |
| `boss_theme.ogg` | [792176](https://freesound.org/s/792176/) | Bellcoda | Looter's Cry |
| `layer_dungeon.ogg` | [422720](https://freesound.org/s/422720/) | Grubzyy | A_Dungeon_Ambience_Loop |
| `layer_underground_breath.ogg` | [789913](https://freesound.org/s/789913/) | newlocknew | DSGNDron_Underground Eerie Breathing Ambience 8_EM |
| `layer_cave_sines.ogg` | [478812](https://freesound.org/s/478812/) | IanStarGem | ambience 4 |

Every file here is Vorbis, converted from the downloads by hand: 161 MB of WAV
became 12 MB, in stereo. The conversions are also in `assets_src/audio/` as
`1`..`6`, in the table's order -- but that directory is gitignored and the WAV
originals are no longer on disk, so **the Freesound IDs above are the only
source of record**. Re-download from them before editing any of these, and put
the untouched file in `assets_src/audio/` first.

## None of them looped, and finding that out took two instruments

**Level at the seam.** RMS of the first and last half-second against the RMS of
the whole file. A track that fades in starts quiet and ends loud, so looping it
drops and swells once per cycle. Three of the six failed badly, and they were
the three picked for the acts:

| File | level at the seam |
|---|---|
| `act1_forgotten_crypts` | **127.4 dB** -- it opens on digital silence |
| `act2_sunken_catacombs` | 42.6 dB |
| `act3_wardens_sanctum` | 26.7 dB |
| `layer_underground_breath` | 11.8 dB |
| `layer_cave_sines` | 0.9 dB |
| `layer_dungeon` | 0.8 dB |

**The step across the seam**, which is what hears a click: the jump from the
last sample to the first, against the 99th-percentile sample-to-sample step in
the body. This is the better instrument, and it caught a file the level test had
already cleared -- `layer_cave_sines` measured 0.9 dB on level and **2.36x** on
the step. Level says nothing about phase.

## The fix is a loop point, not a cut

An earlier pass trimmed the fade-in and crossfaded the ends. That worked, and it
is the wrong tool here: it re-encodes the audio to fix a property of *playback*.
Godot's Vorbis importer takes a `loop_offset`, so the file plays through its
fade-in once -- which is what a fade-in is for, at the start of an act -- and
every loop after returns to a point chosen to match the end.

The offsets were found on the WAV originals, since the OGGs are re-encodes of
the same audio and cannot be analysed without a Vorbis decoder. Candidates were
filtered to those within 2 dB of the file's tail level, then the closest sample
value was picked:

| File | loop returns to | level vs the tail | step |
|---|---|---|---|
| `act1_forgotten_crypts` | 2.590s | +0.0 dB | 0.00x |
| `act2_sunken_catacombs` | 7.010s | +1.9 dB | 0.00x |
| `act3_wardens_sanctum` | 6.283s | -1.9 dB | 0.00x |
| `layer_cave_sines` | 0.651s | +1.7 dB | 0.00x |
| `layer_underground_breath` | 1.005s | -1.7 dB | 0.00x |

`layer_dungeon.ogg` needs no offset: it measured 0.8 dB and 0.11x as downloaded,
so it loops from zero, and `loop_offset` stays 0.

`tools/audio/loopify.py` is the trim-and-crossfade tool, kept for a file that
needs surgery rather than a loop point -- one whose ends genuinely do not match
anywhere.

## What is actually wired

`AudioManager` plays four of these: `act1`, `act2`, `act3` by position in the
run, and `boss_theme` while the Warden lives. The three `layer_*` files are in
the project and nothing plays them -- they are candidates for a second, thinner
layer over the act bed, not content in the game. This project has a name for a
declared thing with no mechanism behind it, and has closed five of them; this
one is written down rather than left to be discovered.

## The one claim here that is not measured

`boss_theme.ogg` has `loop=true`, `loop_offset=0`, and its seam has **not** been
measured: it is Vorbis and it has no WAV original to read. Its author calls it a
loop. If the boss fight runs past 2:06 and something thumps, that is where to
look first.
