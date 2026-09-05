## Round 1 — 2026-09-04

**Implementation soundness: 7/10**

Runtime-verified: windowed capture (`--script=5:move_right:press,20:jump,35:jump,70:dash`)
produced `Landed` (spawn contact), `Jumped`, `DoubleJumped`, `Dashed` — every
`Emit()` call logged `pushBuffer success=True`, zero `PushWarning`s, zero
exceptions/errors in stdout. Buffer sizing is safe: largest emitted buffer
was `Dashed` at 7938 frames (0.18s), well under the ring capacity implied by
`VoiceBufferLength=0.6s` at 44100Hz (`framesAvailableBeforePush=32767` on
every call, confirming no near-overflow).

Envelope shaping is inconsistent by design, not by accident: `GenerateRisingBlip`
uses a true bell envelope (`Mathf.Sin(Mathf.Pi * t)`, 0→1→0) so jump-family
sounds fade in and out cleanly. `GenerateNoiseBurst`/`GenerateThud`/`GenerateClick`
instead start at `envelope(0)=1` (instant attack) — acceptable for
noise/percussive material (real impacts/whooshes have hard onsets) and the
sine-based `Thud` avoids a click anyway because `phase` starts near 0. All
four generators decay to ~0 by `t=1` (verified: `exp(-8)`≈0.0003, `exp(-9)`≈0.0001,
`exp(-26)`/`exp(-40)`≈0), so no hard cutoff on the tail end either.

Two real gaps: (1) `Emit()`'s round-robin (`_nextVoice = (_nextVoice+1) % 8`)
never checks whether the target voice is still playing — under a rapid
combo (several `EnemyDamaged` in <8 voice-lifetimes) it will `Stop()` and
steal a still-sounding voice, audible as a truncated cutoff, not the
graceful pooling the class doc-comment implies. (2) `GenerateNoiseBurst`
is unfiltered white noise with only an amplitude envelope — a real
whoosh/dash sound is normally band-limited or swept in cutoff frequency;
pure white noise will read as a hiss/static burst rather than an air-whoosh.

**Differentiation quality: 6/10**

Cross-family separation is good: `Dashed` is the only pure-noise event (no
tonal content at all) so it stands apart from everything else; `Landed` is
the only falling-pitch, sub-100Hz event (110→50Hz) against an otherwise
all-rising-or-flat set; `WallJumped` mixes in 25% noise which gives it a
grittier texture than the pure-tone `Jumped`.

Two pairs are weak. `Jumped` (500→900Hz, 0.12s, pure tone) vs `DoubleJumped`
(750→1300Hz, 0.10s, pure tone) share the identical rising-blip shape and an
overlapping frequency band (750-900Hz falls inside both sweeps), with only
a 20ms duration difference — these will read as "the same sound, slightly
higher," not two distinct cues. `PlayerDamaged` vs `EnemyDamaged` share the
same 60% noise / 40% tone click formula; since noise dominates the mix in
both and unfiltered white noise doesn't change character with decay rate,
the only real differentiator is the underlying tone (220Hz vs 900Hz)
buried under a majority-noise signal, plus a 90ms-vs-50ms duration gap that
is subtle at click timescales. In fast play (hit combos), these two will
be hard to tell apart by ear.

**Specific issues found (ranked by severity):**
1. `Emit()`'s round-robin voice pool doesn't check `player.Playing` before
   stealing a voice — under >8 events in-flight (realistic for a hit combo:
   several `EnemyDamaged` plus movement SFX) a still-sounding voice gets
   cut off mid-decay. Either check `Playing`/prefer idle voices first, or
   accept and document the truncation as intentional voice-stealing.
2. `Jumped` and `DoubleJumped` use the same waveform shape with overlapping
   frequency ranges and near-identical duration — weakest differentiation
   pair in the set; a wider frequency gap or a shape change (e.g. add a
   short noise transient to DoubleJumped) would separate them more reliably.
3. `PlayerDamaged`/`EnemyDamaged` differ mainly in a tone frequency that's
   underweighted (40%) beneath dominant, unfiltered white noise — the
   noise component itself is indistinguishable between the two.
4. `GenerateNoiseBurst` (dash) is unshaped full-spectrum white noise; a
   swept bandpass/lowpass on the noise would read as a more plausible
   "whoosh" than a flat-spectrum burst.

**Recommendation:** No blocking runtime bugs — the manager is safe and
functional as verified by the capture log. Before the next round, widen
the Jumped/DoubleJumped frequency separation, add voice-stealing protection
(check `Playing` before reuse) for combo scenarios, and consider filtering
the noise-burst generator so Dashed reads as a whoosh rather than static.
