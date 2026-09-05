using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Audio;

/// TEST-SCENE ONLY: the voice pool actually recycles voices.
///
/// The pool's stated promise is that ordinary play spreads across voices
/// "without ever cutting off a still-sounding one", stealing only when a burst
/// genuinely outruns it. That promise was void: AudioStreamPlayer.Playing never
/// goes false for an AudioStreamGenerator -- the stream does not end, it just
/// emits silence -- so after the first eight sounds every voice looked busy and
/// every later sound stole one. Found in a real playthrough, not by a check:
/// twelve sounds, five steals, with the events seconds apart.
///
/// So this fires far more sounds than there are voices, spaced well beyond a
/// sound's length, and asserts no steal happens. Two sounds are then fired
/// back to back per frame to prove the steal path still exists for real
/// bursts -- a pool that never steals would be a pool that drops sounds.
public partial class VoicePoolTest : Node
{
    private const int SpacedSounds = 20;

    /// Spaced by WALL time, not by frames.
    ///
    /// The pool reserves a voice for the real duration of its sound, which is
    /// correct -- audio plays in wall time whatever the engine's step is doing.
    /// The first version of this test spaced sounds 20 frames apart and called
    /// that a third of a second; under --fixed-fps in headless a frame takes
    /// about a millisecond of wall time, so twenty of them are twenty
    /// milliseconds and every voice was still legitimately playing. It reported
    /// 8 steals and looked like a broken pool. The engine was right and the
    /// test's model of time was wrong -- the same "frames are not seconds"
    /// mistake this project already has written down.
    private const ulong MsecApart = 260;  // longest SFX is 160ms

    private int _f;
    private int _fired;
    private int _stealsWhenSpaced;
    private int _stealsInBurst;
    private bool _burstPhase;
    private ulong _lastFireMsec;
    private bool _baselined;
    private int _stealsAtStart;

    public override void _Process(double delta)
    {
        _f++;
        if (EventBus.Instance == null) return;
        // Baseline: the counter is a lifetime total and the game makes sounds
        // while the scene is coming up.
        if (!_baselined) { _baselined = true; _stealsAtStart = AudioManager.VoiceStealCount; _lastFireMsec = Time.GetTicksMsec(); }

        ulong now = Time.GetTicksMsec();
        if (!_burstPhase && now - _lastFireMsec >= MsecApart && _fired < SpacedSounds)
        {
            _fired++;
            _lastFireMsec = now;
            EventBus.Instance.EmitLanded();
        }

        if (_fired >= SpacedSounds && !_burstPhase)
        {
            _burstPhase = true;
            _stealsWhenSpaced = AudioManager.VoiceStealCount - _stealsAtStart;
            GD.Print($"[VOICE] {SpacedSounds} sounds fired {MsecApart}ms apart: " +
                     $"{_stealsWhenSpaced} steals");
        }

        // Burst: many more sounds than voices, all within one sound's lifetime.
        if (_burstPhase && now - _lastFireMsec >= MsecApart)
        {
            for (int i = 0; i < 16; i++) EventBus.Instance.EmitLanded();
            _stealsInBurst = AudioManager.VoiceStealCount - _stealsAtStart - _stealsWhenSpaced;

            GD.Print($"[VOICE] burst of 16 within one frame: {_stealsInBurst} steals");

            bool ok = _stealsWhenSpaced == 0   // spaced play never cuts a voice off
                   && _stealsInBurst > 0;      // and the steal path is still reachable

            GD.Print(ok
                ? "[VOICE] RESULT: PASS (voices recycle when their sound ends; stealing is a burst path, not the norm)"
                : "[VOICE] RESULT: FAIL");
            GetTree().Quit();
        }
    }
}
