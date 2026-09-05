using Godot;

namespace LostCrownlike.Audio;

/// TEST-SCENE ONLY: the ambience runs, and it does not eat the gameplay voices.
///
/// Between hits the dungeon was silent, which reads as the game being off
/// rather than as quiet. The bed is a drone pushed in chunks as its buffer
/// drains, so the failure mode is not "no sound" but "sound for two seconds
/// and then nothing" -- a buffer that runs dry looks identical to working code
/// for the first second of any check that is too short.
///
/// So this watches for SEVERAL SECONDS, and separately asserts the ambience
/// took none of the eight pooled voices: they exist for gameplay cues, and a
/// drone holding one permanently would starve the pool exactly the way the
/// original never-released-voice bug did.
public partial class AmbienceTest : Node
{
    private int _f;
    private AudioStreamPlayer _amb;
    private int _stealsAtStart;
    private int _framesPlaying;
    private int _refills;
    private bool _reported;
    private int _lastAvailable = -1;
    private ulong _startMsec;

    public override void _Process(double delta)
    {
        _f++;

        if (_f == 20)
        {
            _stealsAtStart = AudioManager.VoiceStealCount;
            _startMsec = Time.GetTicksMsec();
            var mgr = GetTree().Root.GetNodeOrNull("AudioManager");
            _amb = mgr?.GetNodeOrNull<AudioStreamPlayer>("Ambience");
            if (_amb == null) { GD.Print("[AMB] RESULT: FAIL (no ambience voice)"); GetTree().Quit(); }
            return;
        }
        if (_amb == null) return;

        if (_amb.Playing) _framesPlaying++;

        // Count refills: the buffer draining and being topped up is the whole
        // mechanism, and a drone that is "playing" but never refilled is silent.
        if (_amb.GetStreamPlayback() is AudioStreamGeneratorPlayback pb)
        {
            int avail = pb.GetFramesAvailable();
            if (_lastAvailable >= 0 && avail < _lastAvailable - 1000) _refills++;
            _lastAvailable = avail;
        }

        // Ended on WALL time, not on a frame count. The generator drains its
        // buffer in real seconds whatever the engine's step is doing, so under
        // --fixed-fps in headless 680 frames of game time is about one second of
        // wall time -- long enough for a single refill, which failed a working
        // bed. Same trap as the voice pool: frames are not seconds.
        if (Time.GetTicksMsec() - _startMsec >= 5000UL && !_reported)
        {
            _reported = true;
            int steals = AudioManager.VoiceStealCount - _stealsAtStart;
            GD.Print($"[AMB] playing on {_framesPlaying} frames over 5s of wall time, buffer refilled {_refills} times");
            GD.Print($"[AMB] gameplay voices stolen by the ambience: {steals}");

            bool ok = _framesPlaying > 200   // continuous, not a one-shot
                   && _refills >= 2          // and actually being fed
                   && steals == 0;           // without touching the pool

            GD.Print(ok
                ? "[AMB] RESULT: PASS (the bed runs continuously on its own voice)"
                : "[AMB] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
