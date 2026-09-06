p = 'src/Audio/AudioManager.cs'
s = open(p, encoding='utf-8').read()

old = '''    public override void _Process(double delta) => TickAmbience(delta);'''
new = '''    // ------------------------------------------------------------------
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

    /// Natural minor, two octaves, as frequency ratios over the root. The
    /// figure walks this set rather than an arpeggio of one chord: a repeating
    /// arpeggio is what makes generated music sound generated.
    private static readonly double[] MinorSteps =
    {
        1.0, 1.122, 1.189, 1.335, 1.498, 1.587, 1.782, 2.0, 2.245, 2.378, 2.670,
    };

    private AudioStreamPlayer _musicVoice;
    private double _musicPadPhaseA, _musicPadPhaseB, _musicNotePhase;
    private double _musicStepTimer;
    private int _musicStep;
    private double _musicNoteAge = 99.0;

    /// 0 at the start of a run, 1 by the last room. Drives tempo and how much
    /// of the pad is audible.
    public float MusicIntensity { get; private set; }

    /// Set while the boss is alive. The figure drops a semitone-ish and the
    /// pad gains a beating fifth -- the point is that it is recognisably not
    /// the corridor music, not that it is a different piece.
    public bool MusicIsBossTheme { get; private set; }

    /// The note currently sounding, in Hz. A test seam: what a check needs to
    /// know is that the notes CHANGE, and reading back generated audio to find
    /// that out would measure the mixer rather than the music.
    public float MusicNoteHz { get; private set; }

    /// How many notes have been played since boot. Monotonic.
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

    private void OnRoomEnteredForMusic(int index, int total)
    {
        MusicIntensity = total <= 1 ? 0f : Mathf.Clamp((float)index / (total - 1), 0f, 1f);
    }

    private void OnBossStateForMusic(bool alive, float healthFraction, bool armoured, bool enraged)
    {
        MusicIsBossTheme = alive;
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

        double root = MusicIsBossTheme ? 82.4 : 98.0;   // E2 against G2
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
                _musicStep += _rng.Next(-3, 4);
                _musicStep = Mathf.Clamp(_musicStep, 0, MinorSteps.Length - 1);
                MusicNoteHz = (float)(root * 4.0 * MinorSteps[_musicStep]);
                _musicNotePhase = 0.0;
            }

            _musicNoteAge += inv;

            // Pluck: a decaying sine with a touch of its own second harmonic,
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
    }'''
assert old in s
s = s.replace(old, new, 1)

old = '''        _accentVoice = MakeExtraVoice("Accent", AmbienceVolumeDb + 4f);'''
new = '''        _accentVoice = MakeExtraVoice("Accent", AmbienceVolumeDb + 4f);
        _musicVoice = MakeMusicVoice();'''
assert old in s
s = s.replace(old, new, 1)

old = '''            EventBus.Instance.Parried += OnParried;'''
new = '''            EventBus.Instance.Parried += OnParried;
            EventBus.Instance.RoomEntered += OnRoomEnteredForMusic;
            EventBus.Instance.BossStateChanged += OnBossStateForMusic;'''
assert old in s
s = s.replace(old, new, 1)

old = '''            EventBus.Instance.Parried -= OnParried;'''
new = '''            EventBus.Instance.Parried -= OnParried;
            EventBus.Instance.RoomEntered -= OnRoomEnteredForMusic;
            EventBus.Instance.BossStateChanged -= OnBossStateForMusic;'''
assert old in s
s = s.replace(old, new, 1)

open(p, 'w', encoding='utf-8', newline='\n').write(s)
print("ok")
