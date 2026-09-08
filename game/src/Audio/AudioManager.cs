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

    /// The melody draws from its OWN generator, and that is not tidiness.
    /// Sharing one Random put the tune downstream of everything else that draws
    /// from it: the ambience drips, and the noise term in every jump, hit, land
    /// and dash. Those are scheduled on `delta` and on what the player does,
    /// while the notes advance on audio frames PUSHED, so how many draws fell
    /// between two notes depended on the wall clock and on the fight -- and a
    /// busy second of combat quietly rewrote the tune.
    private Random _musicRng = new();

    /// Test hook. Null seed restores unseeded behaviour. Both generators are
    /// set: seeding only the effects one would leave the melody drifting, which
    /// is the defect above.
    public void SetRandomSeedForTest(int? seed)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        // Offset so the two streams are independent rather than identical.
        _musicRng = seed.HasValue ? new Random(seed.Value + 1) : new Random();
    }

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
        EventBus.Instance.RoomEntered += OnRoomEnteredForMusic;
        EventBus.Instance.RoomShape += OnRoomShape;
        EventBus.Instance.BossStateChanged += OnBossStateForMusic;
        EventBus.Instance.AttackTelegraphed += OnAttackTelegraphed;
        EventBus.Instance.HeatChanged += OnHeatChangedForWarning;
        EventBus.Instance.PlayerHealthChanged += OnHealthForHeartbeat;
        EventBus.Instance.PlayerDied += OnDiedForHeartbeat;

        _ambienceVoice = MakeExtraVoice("Ambience", AmbienceVolumeDb);
        _accentVoice = MakeExtraVoice("Accent", AmbienceVolumeDb + 4f);
        _musicVoice = MakeMusicVoice();
        _nextDripIn = DripEverySecondsMin;

        GD.Print($"AudioManager: ready, {VoiceCount} voices allocated, subscribed to EventBus signals.");
    }

    public override void _ExitTree()
    {
        // Before the EventBus guard below, and deliberately. A bed holds a
        // loaded AudioStream, and a stream still attached to a player at
        // shutdown is a resource the engine reports as still in use -- measured
        // 3 runs out of 3 with beds on and 0 out of 3 with them off, which is
        // what turned the boot check red. The generated voices never showed
        // this because an AudioStreamGenerator is built here rather than
        // loaded, so nothing outside held it.
        foreach (var bed in new[] { _bedA, _bedB, _layerA, _layerB })
        {
            if (bed == null || !IsInstanceValid(bed)) continue;
            bed.Stop();
            bed.Stream = null;
        }
        _bedA = _bedB = _layerA = _layerB = null;
        _bedPlaying = _bedWanted = _layerPlaying = _layerWanted = "";

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
        EventBus.Instance.RoomEntered -= OnRoomEnteredForMusic;
        EventBus.Instance.RoomShape -= OnRoomShape;
        EventBus.Instance.BossStateChanged -= OnBossStateForMusic;
        EventBus.Instance.AttackTelegraphed -= OnAttackTelegraphed;
        EventBus.Instance.HeatChanged -= OnHeatChangedForWarning;
        EventBus.Instance.PlayerHealthChanged -= OnHealthForHeartbeat;
        EventBus.Instance.PlayerDied -= OnDiedForHeartbeat;
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

        // The drone is pushed in chunks as the buffer drains, so it never ends --
        // unless a recorded bed has taken over the job, which is what the drone
        // was standing in for. The generator still has to be drained or its
        // buffer fills and the voice reports starvation to anything watching.
        if (!_ambienceVoice.Playing) _ambienceVoice.Play();
        if (_ambienceVoice.GetStreamPlayback() is AudioStreamGeneratorPlayback drone)
        {
            int room = drone.GetFramesAvailable();
            if (room > MixRate / 4)
            {
                if (_bedPlaying.Length > 0) PushSilence(drone, room);
                else PushDrone(drone, room);
            }
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

    /// Keeps the generator voice fed while a recorded bed carries the room.
    /// Silence still has to be pushed: an AudioStreamGenerator whose buffer is
    /// never drained reads as starved, and check 48 watches for exactly that.
    private static void PushSilence(AudioStreamGeneratorPlayback playback, int frames)
    {
        var buf = new Vector2[frames];
        playback.PushBuffer(buf);
    }

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

    // ------------------------------------------------------------------
    // Music
    // ------------------------------------------------------------------
    //
    // A run is ten rooms and twelve minutes. The drone above is a room tone --
    // deliberately dull, and correct for that job -- but twelve minutes of one
    // held chord is not a score, it is a hum, and the difference shows up as
    // the game feeling unfinished rather than quiet.
    //
    // Generated, like everything else here: no sample files, per
    // ARCHITECTURE.md. A slow minor figure with a pad under it, on its own
    // voice so the gameplay pool is never spent on it. Two things it must not
    // do: repeat a phrase often enough to be recognised, and get louder than
    // the sword.

    [Export] public bool PlayMusic = true;
    [Export] public float MusicVolumeDb = -26f;

    /// Seconds per note at the calmest. It speeds up as the run goes on.
    [Export] public float MusicStepSecondsSlow = 1.15f;
    [Export] public float MusicStepSecondsFast = 0.62f;

    /// Natural minor over two octaves, as ratios over the root. The figure
    /// WALKS this set rather than arpeggiating one chord: a repeating arpeggio
    /// is most of what makes generated music sound generated.
    private static readonly double[] MinorSteps =
    {
        1.0, 1.122, 1.189, 1.335, 1.498, 1.587, 1.782, 2.0, 2.245, 2.378, 2.670,
    };

    private AudioStreamPlayer _musicVoice;
    private double _musicPadPhaseA, _musicPadPhaseB, _musicNotePhase;
    private double _musicStepTimer;
    /// Starts in the middle of the range, not at the bottom: a walk that
    /// begins at an end spends its first bars against that end.
    private int _musicStep = 5;
    private double _musicNoteAge = 99.0;

    /// 0 at the start of a run, 1 by the last room. Drives tempo and how much
    /// of the pad is audible.
    public float MusicIntensity { get; private set; }

    /// Set while the boss is alive. The root drops and the pad beats harder --
    /// the point is that it is recognisably not the corridor music, not that
    /// it is a different piece.
    public bool MusicIsBossTheme { get; private set; }

    /// The note currently sounding, in Hz. A test seam: what a check needs to
    /// know is that the notes CHANGE, and reading generated audio back to find
    /// that out would measure the mixer rather than the music.
    public float MusicNoteHz { get; private set; }

    /// Notes played since boot. Monotonic.
    public int MusicNotesPlayed { get; private set; }

    private AudioStreamPlayer MakeMusicVoice()
    {
        var p = new AudioStreamPlayer
        {
            Name = "Music",
            VolumeDb = MusicVolumeDb,
            Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 2.5f },
        };
        AddChild(p);
        return p;
    }

    private int _lastRoom, _lastRoomTotal = 1;

    private void OnRoomEnteredForMusic(int index, int total)
    {
        MusicIntensity = total <= 1 ? 0f : Mathf.Clamp((float)index / (total - 1), 0f, 1f);
        _lastRoom = index; _lastRoomTotal = total;
        // The boss bed outranks the act's, and the boss room is inside act III:
        // entering it must not pull the atmosphere back off the fight.
        if (!MusicIsBossTheme) WantBed(BedForRoom(index, total));
    }

    private void OnBossStateForMusic(bool alive, float healthFraction, bool armoured, bool enraged)
    {
        MusicIsBossTheme = alive;
        // Both directions, and the second one had to be added. The boss theme
        // outstaying the boss is what a player hears after the kill -- the room
        // is won and the music is still fighting.
        WantBed(alive ? "boss_theme" : BedForRoom(_lastRoom, _lastRoomTotal));
    }

    private void TickMusic(double delta)
    {
        if (!PlayMusic || _musicVoice == null) return;
        if (!_musicVoice.Playing) _musicVoice.Play();
        if (_musicVoice.GetStreamPlayback() is not AudioStreamGeneratorPlayback playback) return;

        int frames = playback.GetFramesAvailable();
        if (frames <= MixRate / 4) return;

        double stepSeconds = Mathf.Lerp(MusicStepSecondsSlow, MusicStepSecondsFast, MusicIntensity);
        if (MusicIsBossTheme) stepSeconds *= 0.75;

        double root = MusicIsBossTheme ? 82.4 : 98.0;
        double padDetune = MusicIsBossTheme ? 1.5 : 0.7;

        var buf = new Vector2[frames];
        double inv = 1.0 / MixRate;

        for (int i = 0; i < frames; i++)
        {
            _musicStepTimer -= inv;
            if (_musicStepTimer <= 0.0)
            {
                _musicStepTimer += stepSeconds;
                _musicNoteAge = 0.0;
                MusicNotesPlayed++;

                // A walk, not a sequence: each note steps up to three degrees
                // from the last, so the figure wanders instead of looping.
                //
                // It REFLECTS at the ends rather than clamping. Clamping looks
                // equivalent and is not: starting at degree 0, every downward
                // step was truncated to 0, so the figure sat on the bottom note
                // and six seconds of it produced two distinct pitches. Measured
                // that way -- and it made the check flaky as well as the music
                // dull, because "at least three pitches" was a coin flip.
                int next = _musicStep + _musicRng.Next(-3, 4);
                int top = MinorSteps.Length - 1;
                if (next < 0) next = -next;
                if (next > top) next = 2 * top - next;
                _musicStep = Mathf.Clamp(next, 0, top);
                MusicNoteHz = (float)(root * 4.0 * MinorSteps[_musicStep]);
                OnMusicNoteForTest?.Invoke(MusicNoteHz);
                _musicNotePhase = 0.0;
            }

            _musicNoteAge += inv;

            // Pluck: a decaying sine with a little of its own second harmonic,
            // which is enough to read as an instrument rather than a test tone.
            double env = Math.Exp(-_musicNoteAge * 3.4);
            _musicNotePhase += 2.0 * Math.PI * MusicNoteHz * inv;
            double note = (Math.Sin(_musicNotePhase) + 0.28 * Math.Sin(_musicNotePhase * 2.0)) * env * 0.30;

            // Pad: the root and a detuned partner, brought in as the run goes
            // on so the early rooms stay sparse.
            _musicPadPhaseA += 2.0 * Math.PI * root * inv;
            _musicPadPhaseB += 2.0 * Math.PI * (root + padDetune) * inv;
            double pad = (Math.Sin(_musicPadPhaseA) + Math.Sin(_musicPadPhaseB))
                         * 0.055 * (0.35 + 0.65 * MusicIntensity);

            float v = (float)(note + pad);
            buf[i] = new Vector2(v, v);
        }

        playback.PushBuffer(buf);
    }

    public override void _Process(double delta)
    {
        // Before the beds, because TickBeds reads the duck offset when it
        // writes the volumes -- ticked afterwards it would apply one frame late,
        // which on a 120ms effect is a tenth of its whole length.
        TickDuck(delta);
        TickWarnings(delta);
        TickAmbience(delta);
        TickMusic(delta);
        TickBeds(delta);
    }

    // ---- Recorded ambience beds -------------------------------------------
    //
    // Three CC0 atmospheres, one per act, and a fourth for the boss. They
    // REPLACE the synthesised drone rather than layering over it: two
    // continuous ambient sources on the same bus is mud, and the drone was
    // always a stand-in for exactly this. The drips stay -- those are events,
    // and they are what stops a bed from being wallpaper.
    //
    // The melody stays too. It is the thing that answers to the run, and a
    // recording cannot: `MusicIntensity` rises room by room and the boss theme
    // swaps. A bed is a place; the melody is what is happening in it.

    [Export] public bool PlayBeds = true;
    [Export] public float BedVolumeDb = -16f;
    /// Long enough to read as a change of place rather than an edit.
    [Export] public float BedCrossfadeSeconds = 2.5f;

    private const string BedDir = "res://assets/audio/";
    private AudioStreamPlayer _bedA, _bedB;
    private bool _bedBIsActive;
    private double _bedFade = 1.0;
    private string _bedWanted = "", _bedPlaying = "";

    /// TEST SEAM. Which bed is sounding, by file stem. Empty before the first
    /// room. The act boundaries are a rule, not a table, so a check can read
    /// them back instead of a human trusting them.
    public string CurrentBedName => _bedPlaying;

    /// Act from position in the run rather than from a room number, because
    /// RunLength is an [Export]: at ten rooms this is 0-3, 4-6, 7-9, which is
    /// the progression STATUS.md describes, and it still divides into thirds if
    /// the run is retuned.
    public static string BedForRoom(int index, int total)
    {
        float t = total <= 1 ? 0f : Mathf.Clamp((float)index / (total - 1), 0f, 1f);
        return t < 0.4f ? "act1_forgotten_crypts"
             : t < 0.7f ? "act2_sunken_catacombs"
             : "act3_wardens_sanctum";
    }

    private void WantBed(string stem)
    {
        if (!PlayBeds || stem == _bedWanted) return;
        _bedWanted = stem;
    }

    private static AudioStream LoadBed(string stem)
    {
        foreach (var ext in new[] { ".wav", ".ogg" })
            if (ResourceLoader.Exists(BedDir + stem + ext))
                // CacheMode.Ignore, not the default. A cached AudioStream is
                // still held by ResourceLoader after the player releases it, and
                // the engine reports it as "1 resources still in use at exit" --
                // measured 3 of 3 boots with beds on, 0 of 3 with them off, and
                // clearing the player's Stream in _ExitTree did not touch it
                // because the cache, not the player, was the owner.
                return ResourceLoader.Load<AudioStream>(BedDir + stem + ext, "",
                                                        ResourceLoader.CacheMode.Ignore);
        return null;
    }

    // ---- The second layer -------------------------------------------------
    //
    // The bed says which ACT you are in. The layer says what the ROOM is: a
    // corridor, an open drop, a shaft. It is quieter than the bed by design --
    // a second voice at the same level is not a layer, it is a fight.
    //
    // Ten rooms are cycled from three layouts, and the sixth is the first with
    // wider gaps. Nothing can make that untrue from the audio side; what this
    // can do is stop two rooms of the same layout from ALSO sounding identical,
    // because their shapes differ even when their plan does not.

    [Export] public float LayerVolumeDb = -24f;

    private AudioStreamPlayer _layerA, _layerB;
    private bool _layerBIsActive;
    private double _layerFade = 1.0;
    private string _layerWanted = "", _layerPlaying = "";

    /// TEST SEAM. Which second layer is sounding, by file stem.
    public string CurrentLayerName => _layerPlaying;

    /// The room's own shape picks it. Classified in World against the player's
    /// jump -- see DungeonRoomBuilder.ShapeOf -- and crossing as a string so
    /// this file needs to know nothing about level geometry.
    public static string LayerForShape(string shape) => shape switch
    {
        "flat" => "layer_dungeon",
        "climb" => "layer_underground_breath",
        _ => "layer_cave_sines",
    };

    private void OnRoomShape(string shape)
    {
        if (PlayBeds) _layerWanted = LayerForShape(shape);
    }

    private void TickBeds(double delta)
    {
        if (!PlayBeds) return;
        _bedA ??= MakeBedVoice("BedA");
        _bedB ??= MakeBedVoice("BedB");
        _layerA ??= MakeBedVoice("LayerA");
        _layerB ??= MakeBedVoice("LayerB");

        if (_bedWanted.Length > 0 && _bedWanted != _bedPlaying)
        {
            var stream = LoadBed(_bedWanted);
            if (stream != null)
            {
                var incoming = _bedBIsActive ? _bedA : _bedB;
                incoming.Stream = stream;
                incoming.VolumeDb = SilentDb;
                incoming.Play();
                _bedBIsActive = !_bedBIsActive;
                _bedFade = 0.0;
                _bedPlaying = _bedWanted;
            }
            else
            {
                // Say so once rather than fading silently to nothing.
                GD.PushWarning($"AudioManager: no bed asset for '{_bedWanted}'");
                _bedWanted = _bedPlaying;
            }
        }

        if (_bedFade < 1.0)
            _bedFade = Mathf.Min(1.0, _bedFade + delta / Mathf.Max(0.05f, BedCrossfadeSeconds));

        var up = _bedBIsActive ? _bedB : _bedA;
        var down = _bedBIsActive ? _bedA : _bedB;
        float duck = DuckOffsetDb;
        up.VolumeDb = Mathf.Lerp(SilentDb, BedVolumeDb, (float)_bedFade) + duck;
        down.VolumeDb = Mathf.Lerp(BedVolumeDb, SilentDb, (float)_bedFade) + duck;
        if (_bedFade >= 1.0 && down.Playing) down.Stop();

        if (_layerWanted.Length > 0 && _layerWanted != _layerPlaying)
        {
            var stream = LoadBed(_layerWanted);
            if (stream != null)
            {
                var incoming = _layerBIsActive ? _layerA : _layerB;
                incoming.Stream = stream;
                incoming.VolumeDb = SilentDb;
                incoming.Play();
                _layerBIsActive = !_layerBIsActive;
                _layerFade = 0.0;
                _layerPlaying = _layerWanted;
            }
            else
            {
                GD.PushWarning($"AudioManager: no layer asset for '{_layerWanted}'");
                _layerWanted = _layerPlaying;
            }
        }

        if (_layerFade < 1.0)
            _layerFade = Mathf.Min(1.0, _layerFade + delta / Mathf.Max(0.05f, BedCrossfadeSeconds));

        var lUp = _layerBIsActive ? _layerB : _layerA;
        var lDown = _layerBIsActive ? _layerA : _layerB;
        lUp.VolumeDb = Mathf.Lerp(SilentDb, LayerVolumeDb, (float)_layerFade) + duck;
        lDown.VolumeDb = Mathf.Lerp(LayerVolumeDb, SilentDb, (float)_layerFade) + duck;
        if (_layerFade >= 1.0 && lDown.Playing) lDown.Stop();
    }

    /// -60 dB rather than 0 linear: VolumeDb is logarithmic, and lerping to
    /// float.NegativeInfinity produces NaN the moment it is multiplied.
    // -- Ducking under a perfect parry --------------------------------------
    //
    // The hit-stop already stops the world for 120ms; this empties the air
    // underneath it. Pulling the bed and the layer down for the length of the
    // freeze isolates the clang, and letting them back up afterwards is what
    // makes the moment read as a held breath rather than as a dropout.
    //
    // Applied as an OFFSET folded into the volume the mixer already writes
    // every frame, not as a second writer: the bed crossfade assigns VolumeDb
    // unconditionally, so anything setting the level from outside that loop
    // would be overwritten on the next frame and the duck would flicker.
    //
    // SFX are untouched on purpose. The whole point is the clang, and ducking
    // the channel the clang is on would be ducking the thing being isolated.

    [Export] public float ParryDuckDb { get; set; } = -4f;

    /// Held for the length of the perfect-parry freeze, then released. Kept in
    /// step with CombatController.PerfectParryHitStopMs by hand rather than by
    /// reference: Audio holds no compile-time dependency on Combat, and one
    /// number in two files is the smaller price.
    [Export] public float ParryDuckHoldSeconds { get; set; } = 0.12f;

    [Export] public float ParryDuckReleaseSeconds { get; set; } = 0.22f;

    private float _duckHold;
    private float _duckLevel;   // 0 = open, 1 = fully ducked

    /// The offset every background voice is written with. Zero when nothing is
    /// ducking, which is almost always.
    private float DuckOffsetDb => _duckLevel * ParryDuckDb;

    /// Public so a check can assert the duck happened without listening to
    /// anything: "the bed got quieter" is a fact about a number.
    public float CurrentDuckDb => DuckOffsetDb;

    /// <summary>
    /// Advanced with UNSCALED time. The duck exists to sit underneath a
    /// hit-stop, and a hit-stop is Engine.TimeScale at 0.05 -- fed the scaled
    /// delta this timer would run twenty times too slowly and the music would
    /// stay down for two and a half seconds after a parry.
    /// </summary>
    private void TickDuck(double delta)
    {
        float dt = (float)delta / Mathf.Max(0.0001f, (float)Engine.TimeScale);

        if (_duckHold > 0f)
        {
            _duckHold = Mathf.Max(0f, _duckHold - dt);
            _duckLevel = 1f;
            return;
        }

        if (_duckLevel > 0f)
            _duckLevel = Mathf.MoveToward(_duckLevel, 0f, dt / Mathf.Max(0.01f, ParryDuckReleaseSeconds));
    }

    // ---- Warnings: the room is about to go off, and so are you -------------
    //
    // Two cues that are not events but STATES. Everything else in this file
    // fires once when something happens; these repeat while a condition holds,
    // and stop the moment it does not.
    //
    // Both are driven from bus signals alone -- HeatChanged and
    // PlayerHealthChanged -- so this file never asks the world anything. And
    // both allocate only when a beat actually fires: the tick itself is two
    // float subtractions, so a run spent above 25% health with a cold room
    // hands the collector nothing at all.

    /// Matched to the HUD's 3Hz pre-flashover pulse. The ear and the eye are
    /// reporting the same fact and should do it on the same beat, or the two
    /// read as two separate problems.
    [Export] public float HeatSizzleIntervalSeconds { get; set; } = 0.333f;

    /// Where the heat alarm starts, as a fraction of the bar. The same 0.90 the
    /// HUD uses; kept as an export here rather than shared, because Audio holds
    /// no compile-time dependency on UI or World.
    [Export] public float HeatAlarmAt { get; set; } = 0.90f;

    /// About 1.2Hz. Slow, and slower than a real pulse on purpose: a fast one
    /// reads as panic and this has to be legible under a fight.
    [Export] public float HeartbeatIntervalSeconds { get; set; } = 0.833f;

    [Export] public float LowHealthFraction { get; set; } = 0.25f;

    private float _heat01;
    private bool _heatFlashover;
    private float _heatSizzleTimer;

    private float _healthFraction = 1f;
    private bool _playerAlive = true;
    private float _heartbeatTimer;

    private void OnHeatChangedForWarning(float heat01, int state)
    {
        _heat01 = heat01;
        _heatFlashover = state == (int)HeatState.Flashover;
    }

    private void OnHealthForHeartbeat(int current, int max)
    {
        _healthFraction = max <= 0 ? 1f : Mathf.Clamp(current / (float)max, 0f, 1f);
        // A heal back over the line stops the beat, and a respawn restores full
        // health through this same signal -- so nothing has to remember to
        // switch it off.
        if (_healthFraction > LowHealthFraction) _heartbeatTimer = 0f;
        if (current > 0) _playerAlive = true;
    }

    private void OnDiedForHeartbeat() => _playerAlive = false;

    /// <summary>
    /// Advanced with UNSCALED time, like the duck and for the same reason: a
    /// hit-stop is Engine.TimeScale at 0.05, and a warning fed the scaled delta
    /// would stall for the length of every freeze -- which is exactly when the
    /// player most needs to know the room is about to go off.
    /// </summary>
    private void TickWarnings(double delta)
    {
        float dt = (float)delta / Mathf.Max(0.0001f, (float)Engine.TimeScale);

        bool heatAlarm = _heatFlashover || _heat01 >= HeatAlarmAt;
        if (heatAlarm)
        {
            _heatSizzleTimer -= dt;
            if (_heatSizzleTimer <= 0f)
            {
                _heatSizzleTimer = HeatSizzleIntervalSeconds;
                // A short hiss rather than a tone: the floor is about to catch,
                // and fire is broadband. It also puts this cue at the far end
                // of the palette from the gold bell, so an alarm can never be
                // mistaken for an opening.
                Emit(GenerateNoiseBurst(duration: 0.09f, decay: 22f, amplitude: 0.34f), "HeatSizzle");
            }
        }
        else
        {
            _heatSizzleTimer = 0f;
        }

        bool lowHealth = _playerAlive && _healthFraction > 0f && _healthFraction <= LowHealthFraction;
        if (lowHealth)
        {
            _heartbeatTimer -= dt;
            if (_heartbeatTimer <= 0f)
            {
                _heartbeatTimer = HeartbeatIntervalSeconds;
                // 60Hz and nothing else. It sits below every other cue in the
                // game, so it never competes with the fight -- it is felt more
                // than heard, which is what a heartbeat should be.
                Emit(GenerateThud(startFreq: 60f, endFreq: 42f, duration: 0.13f), "Heartbeat");
            }
        }
        else
        {
            _heartbeatTimer = 0f;
        }
    }

    private const float SilentDb = -60f;

    private AudioStreamPlayer MakeBedVoice(string name)
    {
        var p = new AudioStreamPlayer { Name = name, VolumeDb = SilentDb };
        AddChild(p);
        return p;
    }

    private void OnParried(bool perfect, Vector3 atPosition)
    {
        if (!perfect)
        {
            Emit(GenerateClick(duration: 0.07f, decay: 30f, toneFreq: 320f, noiseMix: 0.55f), "Parried");
            return;
        }

        // The air goes out from under it. Set before the clang is emitted so the
        // bed is already down on the frame the sound starts rather than
        // arriving a frame into it.
        _duckHold = ParryDuckHoldSeconds;
        _duckLevel = 1f;

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

    // ---- The impact hierarchy ----------------------------------------------
    //
    // One 900Hz click for every blow in the game meant a jab, a fully charged
    // overhead and a sword bouncing off plate armour all sounded the same. The
    // last of those is the expensive one: a player who cannot HEAR that their
    // blade was turned has no reason to stop swinging, and the armour rule is
    // the one the whole Warden fight rests on.
    //
    // Classified by AMOUNT, because amount is all the bus carries. EnemyDamaged
    // is flattened to primitives and has no telegraph and no "was this
    // absorbed" flag, and widening it for one listener would push a combat
    // detail into every call site that deals damage. The damage values are far
    // enough apart to be read directly: chip is 1-3, a light is 10-14, a heavy
    // 25-27, a charged 55-65.

    /// At or under this, the blade was turned rather than landed. The Warden,
    /// the Sentinel, the Forgemaster and the Slagbound all chip at 1-3.
    [Export] public int AbsorbedDamageCeiling { get; set; } = 3;

    /// Light attacks land 10-14 with the per-combo-step bonus.
    [Export] public int LightDamageCeiling { get; set; } = 18;

    /// Heavies land 25-27. Anything past this is a released charge.
    [Export] public int HeavyDamageCeiling { get; set; } = 40;

    private void OnEnemyDamaged(Node3D enemy, int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical)
    {
        if (amount <= AbsorbedDamageCeiling)
        {
            // TINK. Bright, tiny, and with no body at all -- the sound of a
            // sword skidding off plate. It is deliberately the least satisfying
            // noise in the game: the player should want it to stop.
            //
            // Measured against the whole palette before being chosen, because
            // this exact call is what the sound-separation check exercises: it
            // emits EnemyDamaged with Amount = 1, which lands here. At 45ms /
            // 2391Hz / 0.108 it clears every other cue on at least one axis.
            Emit(GenerateClick(duration: 0.045f, decay: 60f, toneFreq: 2400f, noiseMix: 0.05f), "EnemyDamaged");
            return;
        }

        if (amount <= LightDamageCeiling)
        {
            // A cut: short, bright, mostly tone. Tone-dominant is the inverse
            // of PlayerDamaged's gritty mix, so taking a hit and landing one
            // are told apart by texture rather than by pitch.
            Emit(GenerateClick(duration: 0.04f, decay: 45f, toneFreq: 1100f, noiseMix: 0.25f), "EnemyDamaged");
            return;
        }

        if (amount <= HeavyDamageCeiling)
        {
            // A blow. Lower, longer, and left to ring -- the slow decay is what
            // reads as metal rather than as a louder click.
            Emit(GenerateClick(duration: 0.08f, decay: 18f, toneFreq: 450f, noiseMix: 0.30f), "EnemyDamaged");
            return;
        }

        // A released charge. The only impact in the game with real bass under
        // it, which is the point: it is the most damaging thing the player owns
        // and nothing else should sound like it.
        Emit(GeneratePunch(startFreq: 120f, endFreq: 60f, duration: 0.12f, decay: 16f, noiseAmount: 0.30f), "EnemyDamaged");
    }

    /// <summary>
    /// The three attack channels, as sound.
    ///
    /// This is the pass that lets the player fight without looking at the
    /// enemy: gold says "punish this", red says "get out of the way", and the
    /// two are as far apart as two cues in this game can be -- a bright bell
    /// against a low growl.
    ///
    /// White is SILENT, and deliberately. Every ordinary strike in the game is
    /// white; a cue on all of them would be a rattle under the entire fight and
    /// would bury the two that carry information. The absence is the message.
    /// </summary>
    private void OnAttackTelegraphed(Node3D enemy, int type)
    {
        switch ((AttackTelegraphType)type)
        {
            case AttackTelegraphType.CounterGold:
                // Two partials a fifth apart, swept up. One tone reads as a
                // beep; two read as struck metal, which is what a counterable
                // attack is announcing.
                Emit(GenerateBell(startFreq: 1400f, endFreq: 2200f, duration: 0.16f, partialRatio: 1.5f),
                     "TelegraphGold");
                break;

            case AttackTelegraphType.UnparryableRed:
                // Down, not up, and dirty. Every other cue in this game rises;
                // this one falls, which is the fastest way to say "not that" to
                // a player who has learned the gold bell.
                Emit(GenerateRisingBlip(startFreq: 160f, endFreq: 75f, duration: 0.20f, noiseMix: 0.35f),
                     "TelegraphRed");
                break;
        }
    }

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

    /// <summary>
    /// Two sine partials a fixed ratio apart, swept together under a bell
    /// envelope. A single tone reads as a beep; two read as struck metal, and
    /// the gold channel has to sound like a bell being hit rather than like a
    /// notification.
    ///
    /// The envelope is the blip's sin(pi*t) raised to 0.7, which opens faster
    /// and hangs longer -- a bell's attack is immediate and its tail is the
    /// part you hear.
    /// </summary>
    private float[] GenerateBell(float startFreq, float endFreq, float duration, float partialRatio)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        double p1 = 0.0, p2 = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            float freq = Mathf.Lerp(startFreq, endFreq, t);
            p1 += 2.0 * Math.PI * freq / MixRate;
            p2 += 2.0 * Math.PI * freq * partialRatio / MixRate;
            float envelope = Mathf.Pow(Mathf.Sin(Mathf.Pi * t), 0.7f);
            float tone = (float)Math.Sin(p1) * 0.62f + (float)Math.Sin(p2) * 0.38f;
            buf[i] = Mathf.Clamp(tone * envelope * 0.55f, -1f, 1f);
        }
        return buf;
    }

    /// <summary>
    /// A low sine drop with grit on it and a percussive decay. Thud with a
    /// noise component: the difference between a landing (pure tone) and a
    /// charged sword hitting a body (tone plus impact debris).
    /// </summary>
    private float[] GeneratePunch(float startFreq, float endFreq, float duration, float decay, float noiseAmount)
    {
        int n = (int)(MixRate * duration);
        var buf = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / n;
            phase += 2.0 * Math.PI * Mathf.Lerp(startFreq, endFreq, t) / MixRate;
            float envelope = Mathf.Exp(-decay * t);
            float noise = (float)(_rng.NextDouble() * 2.0 - 1.0);
            float body = (float)Math.Sin(phase) * (1f - noiseAmount) + noise * noiseAmount;
            buf[i] = Mathf.Clamp(body * envelope * 0.8f, -1f, 1f);
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

    /// Test hook, and it exists because polling could not do the job. A note
    /// lasts about a second, but TickMusic fills the WHOLE available buffer in
    /// one call -- up to 2.2 seconds of audio -- so a single frame can start two
    /// or three notes and `MusicNoteHz` only ever holds the last of them. A
    /// harness sampling once per frame therefore sees every note on an idle
    /// machine and misses most of them on a busy one, which is precisely how
    /// the music check passed alone and failed inside a full gate run. Fires
    /// once per note, so a harness counts what was played rather than what it
    /// happened to catch. Null in normal play.
    public static System.Action<float> OnMusicNoteForTest;

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
