using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.Audio;

/// TEST-SCENE ONLY: there is music, it moves, and it answers to the run.
///
/// The dungeon already had a drone. A drone is a room tone, and twelve minutes
/// of one held chord is a hum rather than a score -- which reads as the game
/// being unfinished, not as the game being quiet.
///
/// Three claims, and the first two are the ones a "there is audio" check would
/// miss:
///
///   the voice never runs dry -- a generated stream that is not refilled stops,
///   silently, and stays stopped;
///   the notes actually CHANGE, more than a couple of distinct pitches;
///   the boss is not the corridor.
///
/// Neither frames nor seconds: this waits on NOTES.
///
/// It used to listen for six wall-clock seconds and assert three distinct
/// pitches. Six seconds is about five notes, and the figure is a random WALK
/// with a step of -3..+3 degrees -- so "three distinct out of five" was a
/// throw of the dice, and it came up short inside a full gate run while
/// passing eight times standing alone on the same build. The pitch counts
/// across those runs were 4, 5, 4, 6, 5, 4, 5, 5.
///
/// Seeding the synthesiser -- the seam check 22 uses -- does NOT fix it, and
/// that was measured too: 4, 6, 4, 5, 5. The reason is that AudioManager's one
/// Random is shared with the ambience, whose drips are scheduled on `delta`
/// while the notes advance on audio frames PUSHED, so how many draws the drips
/// take between two notes depends on the wall clock. Two clocks, one
/// generator, and the walk lands somewhere different every run.
///
/// So the window is counted in notes instead. Twelve of them cannot come up
/// under three distinct pitches unless the walk itself is broken, which is the
/// claim being made -- and a run that never reaches twelve is a stopped
/// stream, which the wall-clock cap below reports as the failure it is.
///
/// Counting the window in notes was not enough on its own, and the second gate
/// run said so. The pitches were still gathered by POLLING `MusicNoteHz` once
/// per frame, and TickMusic fills the whole available buffer in one call -- up
/// to 2.2 seconds, two or three notes -- keeping only the last. On an idle
/// machine frames are cheap and every note is seen; inside a gate that has just
/// exported a 208 MB binary they are not, and the poll saw two pitches out of
/// twelve notes. The count now comes from `OnMusicNoteForTest`, which fires
/// once per note, so what is measured is what was played.
public partial class MusicTest : Node
{
    /// Notes, not seconds. Twelve is about fourteen seconds of corridor.
    private const int NotesToHear = 12;

    /// Only a backstop against a stream that has died: at ~1.15s a note,
    /// twelve notes take ~14s, so forty means "these notes are never coming".
    private const double PhaseCapSeconds = 40.0;

    private int _f;
    private AudioManager _audio;
    private DungeonRoomBuilder _room;

    private readonly HashSet<int> _pitchesEarly = new();
    private readonly HashSet<int> _pitchesBoss = new();
    private int _notesAtStart = -1;
    private int _notesAfterListening = -1;
    private int _timesStarved;
    private float _intensityEarly = -1f;
    private float _intensityLate = -1f;
    private bool _bossThemeSeen;

    private ulong _phaseStartedMs;
    private int _phase;

    public override void _Process(double delta)
    {
        _f++;
        if (_audio == null)
        {
            _audio = FindFirst<AudioManager>(GetTree().Root);
            if (_audio != null)
                AudioManager.OnMusicNoteForTest = hz =>
                {
                    if (_phase == 1) _pitchesEarly.Add(Mathf.RoundToInt(hz));
                    else if (_phase == 3) _pitchesBoss.Add(Mathf.RoundToInt(hz));
                };
        }
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_audio == null || _room == null) return;
        if (_room.IsRebuilding) return;

        var voice = _audio.GetNodeOrNull<AudioStreamPlayer>("Music");
        if (voice == null) { Done(false, "the audio manager has no music voice"); return; }

        // A generated stream that is not kept fed stops and never restarts. It
        // shows up as available frames reaching the whole buffer, which is what
        // "nothing is being pushed" looks like from outside.
        if (voice.GetStreamPlayback() is AudioStreamGeneratorPlayback pb
            && voice.Stream is AudioStreamGenerator gen)
        {
            int capacity = Mathf.RoundToInt(gen.BufferLength * gen.MixRate);
            if (_phase > 0 && pb.GetFramesAvailable() >= capacity - 8) _timesStarved++;
        }

        switch (_phase)
        {
            case 0:
                if (_f < 30) return;
                _notesAtStart = _audio.MusicNotesPlayed;
                _intensityEarly = _audio.MusicIntensity;
                Advance();
                return;

            case 1:
                if (_audio.MusicNotesPlayed - _notesAtStart < NotesToHear)
                {
                    if (Elapsed() < PhaseCapSeconds) return;
                    Done(false, $"only {_audio.MusicNotesPlayed - _notesAtStart} notes in " +
                                $"{PhaseCapSeconds:F0}s -- the music stopped");
                    return;
                }
                _notesAfterListening = _audio.MusicNotesPlayed;
                GD.Print($"[MUSIC] {NotesToHear} notes of corridor in {Elapsed():F1}s: notes " +
                         $"{_notesAtStart} -> {_notesAfterListening}, distinct pitches " +
                         $"{_pitchesEarly.Count}, intensity {_intensityEarly:F2}, " +
                         $"starved {_timesStarved}x");
                _room.RebuildAs(_room.RunLength - 1);   // the boss room
                Advance();
                return;

            case 2:
                if (Elapsed() < 1.0) return;
                _intensityLate = _audio.MusicIntensity;
                Advance();
                return;

            case 3:
                if (_audio.MusicIsBossTheme) _bossThemeSeen = true;
                if (_audio.MusicNotesPlayed - _notesAfterListening < NotesToHear)
                {
                    if (Elapsed() < PhaseCapSeconds) return;
                    Done(false, $"only {_audio.MusicNotesPlayed - _notesAfterListening} boss " +
                                $"notes in {PhaseCapSeconds:F0}s -- the music stopped");
                    return;
                }

                GD.Print($"[MUSIC] {NotesToHear} notes of boss room in {Elapsed():F1}s: distinct " +
                         $"pitches {_pitchesBoss.Count}, boss theme engaged {_bossThemeSeen}, " +
                         $"intensity {_intensityLate:F2}");

                bool moved = _notesAfterListening - _notesAtStart >= 3;
                bool varied = _pitchesEarly.Count >= 3;
                bool differs = !_pitchesEarly.SetEquals(_pitchesBoss);
                bool louderLater = _intensityLate > _intensityEarly;

                GD.Print($"[MUSIC] notes advanced={moved} varied={varied} " +
                         $"boss differs from corridor={differs} intensity rose={louderLater}");

                bool ok = moved && varied && differs && louderLater
                       && _bossThemeSeen && _timesStarved == 0;

                Done(ok, ok ? "the music plays continuously, wanders, and changes for the boss" : "");
                return;
        }
    }

    private void Advance() { _phase++; _phaseStartedMs = Time.GetTicksMsec(); }

    private double Elapsed() => (Time.GetTicksMsec() - _phaseStartedMs) / 1000.0;

    private void Done(bool ok, string why)
    {
        AudioManager.OnMusicNoteForTest = null;
        GD.Print(ok ? $"[MUSIC] RESULT: PASS ({why})"
                    : $"[MUSIC] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
