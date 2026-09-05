using System;
using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Audio;

/// <summary>
/// Autoload singleton (see project.godot [autoload], registered after EventBus
/// so EventBus.Instance is already set when this subscribes). Subscribes to
/// EventBus gameplay signals and plays short procedurally synthesized SFX via
/// AudioStreamGenerator — no imported sample files, per ARCHITECTURE.md.
///
/// Each event gets its own waveform recipe (sine sweep, noise burst, or a mix)
/// so the sounds are distinguishable by ear. Playback uses a small round-robin
/// pool of AudioStreamPlayer voices so overlapping events (e.g. Jumped right
/// after Landed) don't cut each other off.
/// </summary>
public partial class AudioManager : Node
{
    public static AudioManager Instance { get; private set; }

    private const int MixRate = 44100;
    private const float VoiceBufferLength = 0.6f; // seconds of ring-buffer capacity per voice
    private const int VoiceCount = 8;

    private readonly List<AudioStreamPlayer> _voices = new();
    private readonly ulong[] _voiceStartedMsec = new ulong[VoiceCount];

    /// When each voice's sound is expected to have finished.
    ///
    /// AudioStreamPlayer.Playing cannot answer this. With an
    /// AudioStreamGenerator the stream never ends -- it runs out of buffered
    /// frames and emits silence -- so Playing stays true from the first Play()
    /// until something stops it, and nothing did. After the first eight sounds
    /// every voice looked busy forever and SelectVoice fell through to the
    /// steal path every single time. Measured in a real playthrough: twelve
    /// sounds, five of them stealing a voice, with the events seconds apart.
    private readonly ulong[] _voiceFreeAtMsec = new ulong[VoiceCount];
    private int _nextVoice;

    /// Test-visible tally. A steal is not an error -- it is the designed
    /// overload path -- so the only way to tell "working pool" from "pool that
    /// steals every time" is to count.
    public static int VoiceStealCount { get; private set; }
    /// Not readonly, and not Godot's RNG: this is a .NET Random with a
    /// time-based seed, so GD.Seed() does not touch it. Left random in play —
    /// identical noise every time would sound mechanical — but a harness must
    /// be able to pin it, or any measurement of the generated waveforms drifts
    /// run to run and the check built on it becomes flaky.
    private Random _rng = new();

    /// Test hook. Null seed restores unseeded behaviour.
    public void SetRandomSeedForTest(int? seed) => _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _Ready()
    {
        for (int i = 0; i < VoiceCount; i++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"Voice{i}",
                Stream = new AudioStreamGenerator
                {
                    MixRate = MixRate,
                    BufferLength = VoiceBufferLength,
                },
            };
            AddChild(player);
            _voices.Add(player);
        }

        if (EventBus.Instance == null)
        {
            GD.PushError("AudioManager: EventBus.Instance is null in _Ready — check autoload order in project.godot (EventBus must precede AudioManager).");
            return;
        }

        EventBus.Instance.Jumped += OnJumped;
        EventBus.Instance.DoubleJumped += OnDoubleJumped;
        EventBus.Instance.Dashed += OnDashed;
        EventBus.Instance.WallJumped += OnWallJumped;
        EventBus.Instance.Landed += OnLanded;
        EventBus.Instance.PlayerDamaged += OnPlayerDamaged;
        EventBus.Instance.EnemyDamaged += OnEnemyDamaged;
        EventBus.Instance.Parried += OnParried;

        _ambienceVoice = MakeExtraVoice("Ambience", AmbienceVolumeDb);
        _accentVoice = MakeExtraVoice("Accent", AmbienceVolumeDb + 4f);
        _nextDripIn = DripEverySecondsMin;

        GD.Print($"AudioManager: ready, {VoiceCount} voices allocated, subscribed to EventBus signals.");
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance == null)
        {
            return;
        }

        EventBus.Instance.Jumped -= OnJumped;
        EventBus.Instance.DoubleJumped -= OnDoubleJumped;
        EventBus.Instance.Dashed -= OnDashed;
        EventBus.Instance.WallJumped -= OnWallJumped;
        EventBus.Instance.Landed -= OnLanded;
        EventBus.Instance.PlayerDamaged -= OnPlayerDamaged;
        EventBus.Instance.EnemyDamaged -= OnEnemyDamaged;
        EventBus.Instance.Parried -= OnParried;
    }

    // ---- EventBus handlers -------------------------------------------------

    private void OnJumped() =>
        Emit(GenerateRisingBlip(startFreq: 500f, endFreq: 900f, duration: 0.12f), "Jumped");

    private void OnDoubleJumped() =>
        // Pushed well above Jumped's 500-900Hz sweep (was 750-1300Hz, overlapping) so the
        // two are clearly distinct pitches rather than "same sound, slightly higher".
        Emit(GenerateRisingBlip(startFreq: 1000f, endFreq: 1600f, duration: 0.10f), "DoubleJumped");

    private void OnDashed() =>
        Emit(GenerateNoiseBurst(duration: 0.18f, decay: 9f, amplitude: 0.45f), "Dashed");

    private void OnWallJumped() =>
        Emit(GenerateRisingBlip(startFreq: 420f, endFreq: 760f, duration: 0.11f, noiseMix: 0.25f), "WallJumped");

    private void OnLanded() =>
        Emit(GenerateThud(startFreq: 110f, endFreq: 50f, duration: 0.16f), "Landed");

    private void OnPlayerDamaged(int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical) =>
        // Noise-dominant (80/20) so it reads as a dull, gritty hit-taken thud.
        // Noise mix pulled back from 0.8 to 0.45. Measured, not guessed: at
        // 0.8 this sound sat at a spectral centroid of 10825Hz with noisiness
        // 0.491, against Dashed's 10876Hz / 0.493 — the two were separable only
        // by duration, the weakest of the three perceptual axes. Keeping the
        // low 220Hz tone dominant makes it a dull body-blow thud against the
        // dash's bright hiss, while still reading as grittier than the
        // tone-dominant EnemyDamaged.
        Emit(GenerateClick(duration: 0.09f, decay: 26f, toneFreq: 220f, noiseMix: 0.45f), "PlayerDamaged");

    /// Parry, and the perfect one. They have to be told apart by EAR, not only
    /// by the blade's colour: the whole point of the tight window is that you
    /// know immediately whether you earned the punish, and looking down at the
    /// sword to find out is exactly the moment you cannot spare.
    ///
    /// Measured rather than intended: the ordinary parry lands at a 7410Hz
    /// centroid with 0.34 noisiness -- a harsh, broadband clash -- and the
    /// perfect one at 1536Hz with 0.07, a clear ringing tone. So the two are
    /// separated by NOISE versus TONE, not by brightness, which is the more
    /// legible difference of the two and the opposite of what was aimed for.
    /// Check 23 puts both through the same separation test as every other cue.
    // ------------------------------------------------------------------
    // Ambience
    // ------------------------------------------------------------------
    //
    // Between hits the dungeon is silent, which reads as the game being off
    // rather than as quiet. This is a bed and an occasional accent, not music:
    // a low drone with a slow beat between two detuned partials, and a rare
    // water drip. Both are deliberately dull -- an ambience you notice is one
    // you will be sick of by room three -- and they use their own voices so the
    // pool that serves gameplay is never spent on them.

    [Export] public bool PlayAmbience = true;
    [Export] public float AmbienceVolumeDb = -22f;
    [Export] public float DripEverySecondsMin = 6f;
    [Export] public float DripEverySecondsMax = 15f;

    private AudioStreamPlayer _ambienceVoice;
    private AudioStreamPlayer _accentVoice;
    private double _nextDripIn;

    private AudioStreamPlayer MakeExtraVoice(string name, float db)
    {
        var p = new AudioStreamPlayer
        {
            Name = name,
            VolumeDb = db,
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 2.2f },
        };
        AddChild(p);
        return p;
    }

    private void TickAmbience(double delta)
    {
        if (!PlayAmbience || _ambienceVoice == null) return;

        // The drone is pushed in chunks as the buffer drains, so it never ends.
        if (!_ambienceVoice.Playing) _ambienceVoice.Play();
        if (_ambienceVoice.GetStreamPlayback() is AudioStreamGeneratorPlayback drone)
        {
            int room = drone.GetFramesAvailable();
            if (room > MixRate / 4) PushDrone(drone, room);
        }

        _nextDripIn -= delta;
        if (_nextDripIn > 0f) return;
        _nextDripIn = _rng.NextDouble() * (DripEverySecondsMax - DripEverySecondsMin) + DripEverySecondsMin;

        if (_accentVoice == null) return;
        _accentVoice.Stop();
        _accentVoice.Play();
        if (_accentVoice.GetStreamPlayback() is AudioStreamGeneratorPlayback accent)
        {
            var drip = GenerateRisingBlip(startFreq: 1500f + (float)_rng.NextDouble() * 700f,
                                          endFreq: 380f, duration: 0.13f);
            var frames = new Vector2[drip.Length];
            for (int i = 0; i < drip.Length; i++) frames[i] = new Vector2(drip[i] * 0.5f, drip[i] * 0.5f);
            accent.PushBuffer(frames);
        }
    }

    private double _dronePhaseA, _dronePhaseB;

    private void PushDrone(AudioStreamGeneratorPlayback playback, int frames)
    {
        // Two partials a couple of hertz apart: the beat between them is what
        // makes a held tone sound like a space rather than a test signal.
        const double A = 54.0, B = 56.7;
        var buf = new Vector2[frames];
        for (int i = 0; i < frames; i++)
        {
            _dronePhaseA += 2.0 * Math.PI * A / MixRate;
            _dronePhaseB += 2.0 * Math.PI * B / MixRate;
            float v = (float)((Math.Sin(_dronePhaseA) + Math.Sin(_dronePhaseB) * 0.8) * 0.11);
            buf[i] = new Vector2(v, v);
        }
        playback.PushBuffer(buf);
    }

    public override void _Process(double delta) => TickAmbience(delta);

    private void OnParried(bool perfect, Vector3 atPosition)
    {
        if (!perfect)
        {
            Emit(GenerateClick(duration: 0.07f, decay: 30f, toneFreq: 320f, noiseMix: 0.55f), "Parried");
            return;
        }

        var clash = GenerateClick(duration: 0.14f, decay: 14f, toneFreq: 520f, noiseMix: 0.35f);
        var ring = GenerateRisingBlip(startFreq: 900f, endFreq: 1750f, duration: 0.14f);
        var mixed = new float[clash.Length];
        for (int i = 0; i < mixed.Length; i++)
        {
            float r = i < ring.Length ? ring[i] : 0f;
            // Kept under 1.0 by construction rather than by hoping: two sounds
            // summed at full amplitude clip, and a clipped parry cue is a click.
            mixed[i] = Mathf.Clamp(clash[i] * 0.7f + r * 0.6f, -0.95f, 0.95f);
        }
        Emit(mixed, "PerfectParried");
    }

    private void OnEnemyDamaged(Node3D enemy, int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical) =>
        // Tone-dominant (20/80), inverse mix from PlayerDamaged, so the two are
        // distinguishable by texture (gritty vs. sharp/ringing), not just by the
        // underlying tone frequency buried under noise.
        Emit(GenerateClick(duration: 0.05f, decay: 40f, toneFreq: 900f, noiseMix: 0.2f), "EnemyDamaged");

    // ---- Waveform synthesis -------------------------------------------------

    /// <summary>Short rising-pitch sine sweep with a smooth bell envelope (attack+decay, no click). Used for jump-family events.</summary>
    private float[] GenerateRisingBlip(float startFreq, float endFreq, float duration, float noiseMix = 0f)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += 2.0 * Math.PI * freq / MixRate;
            float envelope = Mathf.Sin(Mathf.Pi * t); // 0 -> 1 -> 0, avoids start/end clicks
            float sine = (float)Math.Sin(phase);
            float noise = noiseMix > 0f ? (float)(_rng.NextDouble() * 2.0 - 1.0) : 0f;
            buf[i] = Mathf.Clamp((sine * (1f - noiseMix) + noise * noiseMix) * envelope * 0.5f, -1f, 1f);
        }
        return buf;
    }

    /// <summary>White noise with a fast-attack exponential-decay envelope. Whoosh/burst character for dash.</summary>
    private float[] GenerateNoiseBurst(float duration, float decay, float amplitude)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float envelope = Mathf.Exp(-decay * t);
            float noise = (float)(_rng.NextDouble() * 2.0 - 1.0);
            buf[i] = Mathf.Clamp(noise * envelope * amplitude, -1f, 1f);
        }
        return buf;
    }

    /// <summary>Low-frequency sine with a downward pitch drop and fast percussive decay. Low thud for landing.</summary>
    private float[] GenerateThud(float startFreq, float endFreq, float duration)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            phase += 2.0 * Math.PI * freq / MixRate;
            float envelope = Mathf.Exp(-8f * t);
            buf[i] = Mathf.Clamp((float)Math.Sin(phase) * envelope * 0.7f, -1f, 1f);
        }
        return buf;
    }

    /// <summary>Very short noise+tone blend with a very fast decay. Sharp click/impact for hit events.</summary>
    private float[] GenerateClick(float duration, float decay, float toneFreq, float noiseMix = 0.6f)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float envelope = Mathf.Exp(-decay * t);
            float noise = (float)(_rng.NextDouble() * 2.0 - 1.0);
            phase += 2.0 * Math.PI * toneFreq / MixRate;
            float tone = (float)Math.Sin(phase);
            buf[i] = Mathf.Clamp((noise * noiseMix + tone * (1f - noiseMix)) * envelope * 0.6f, -1f, 1f);
        }
        return buf;
    }

    // ---- Playback ------------------------------------------------------------

    /// <summary>
    /// Picks which voice <see cref="Emit"/> should (re)use. Walks the pool starting at
    /// the round-robin cursor and returns the first voice that isn't currently
    /// <c>Playing</c>, so normal play spreads evenly across voices without ever cutting
    /// off a still-sounding one. If every voice is busy (e.g. a hit combo firing more
    /// events than <see cref="VoiceCount"/> within one voice's lifetime), we steal the
    /// oldest-started voice rather than whatever the round-robin index happens to be —
    /// that voice is closest to finishing anyway, so stealing it is the least
    /// perceptible cutoff. This is chosen over simply dropping the new sound because a
    /// dropped hit-reaction cue reads as a missing/broken sound, whereas truncating the
    /// oldest (nearly-done) voice is inaudible in practice.
    /// </summary>
    private int SelectVoice()
    {
        int startIndex = _nextVoice;
        for (int offset = 0; offset < _voices.Count; offset++)
        {
            int candidate = (startIndex + offset) % _voices.Count;
            if (Time.GetTicksMsec() >= _voiceFreeAtMsec[candidate])
            {
                _nextVoice = (candidate + 1) % _voices.Count;
                return candidate;
            }
        }

        int oldest = 0;
        for (int i = 1; i < _voices.Count; i++)
        {
            if (_voiceStartedMsec[i] < _voiceStartedMsec[oldest])
            {
                oldest = i;
            }
        }

        VoiceStealCount++;
        GD.Print($"AudioManager: voice pool exhausted (all {_voices.Count} voices busy) — stealing oldest-started voice {oldest}.");
        _nextVoice = (oldest + 1) % _voices.Count;
        return oldest;
    }

    /// <summary>
    /// Grabs a voice from the pool (idle by preference, see <see cref="SelectVoice"/>),
    /// restarts its generator playback, and pushes the full synthesized buffer in one
    /// call (each SFX is short enough to fit within a voice's ring-buffer capacity, so
    /// no incremental feeding is needed). Logs the push result so buffer fills can be
    /// verified from stdout without listening.
    /// </summary>
    /// Test hook: lets a harness observe the actual buffer produced for an
    /// event, so sound differentiation can be measured rather than assumed.
    /// Null in normal play.
    public static System.Action<string, float[]> OnBufferForTest;

    private void Emit(float[] monoSamples, string eventName)
    {
        OnBufferForTest?.Invoke(eventName, monoSamples);
        int voiceIndex = SelectVoice();
        var player = _voices[voiceIndex];

        player.Stop();
        player.Play();
        ulong now = Time.GetTicksMsec();
        _voiceStartedMsec[voiceIndex] = now;
        // Reserve the voice for exactly as long as the sound lasts, and no
        // longer. A little slack covers the mixer latency between pushing the
        // buffer and the last frame actually leaving it.
        _voiceFreeAtMsec[voiceIndex] = now + (ulong)(monoSamples.Length * 1000.0 / MixRate) + 20UL;

        if (player.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback)
        {
            GD.PushWarning($"AudioManager: '{eventName}' — GetStreamPlayback() did not return an AudioStreamGeneratorPlayback; buffer NOT filled.");
            return;
        }

        var frames = new Vector2[monoSamples.Length];
        for (int i = 0; i < monoSamples.Length; i++)
        {
            frames[i] = new Vector2(monoSamples[i], monoSamples[i]);
        }

        int availableBefore = playback.GetFramesAvailable();
        bool pushed = playback.PushBuffer(frames);

        GD.Print($"AudioManager: '{eventName}' -> voice {voiceIndex}, {frames.Length} frames " +
                  $"({(float)frames.Length / MixRate:F3}s), framesAvailableBeforePush={availableBefore}, pushBuffer success={pushed}");

        if (!pushed)
        {
            GD.PushWarning($"AudioManager: '{eventName}' — PushBuffer reported failure (buffer full?) on voice {voiceIndex}.");
        }
    }
}
