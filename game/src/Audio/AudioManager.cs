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
        up.VolumeDb = Mathf.Lerp(SilentDb, BedVolumeDb, (float)_bedFade);
        down.VolumeDb = Mathf.Lerp(BedVolumeDb, SilentDb, (float)_bedFade);
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
        lUp.VolumeDb = Mathf.Lerp(SilentDb, LayerVolumeDb, (float)_layerFade);
        lDown.VolumeDb = Mathf.Lerp(LayerVolumeDb, SilentDb, (float)_layerFade);
        if (_layerFade >= 1.0 && lDown.Playing) lDown.Stop();
    }

    /// -60 dB rather than 0 linear: VolumeDb is logarithmic, and lerping to
    /// float.NegativeInfinity produces NaN the moment it is multiplied.
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
