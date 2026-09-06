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
/// Frames are not seconds here either: the note interval is about a second of
/// WALL time, and under --fixed-fps a frame is roughly a millisecond, so this
/// waits on the clock rather than on a frame count. That mistake has been made
/// three times in this suite.
public partial class MusicTest : Node
{
    private const double ListenSeconds = 6.0;

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
        _audio ??= FindFirst<AudioManager>(GetTree().Root);
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
                _pitchesEarly.Add(Mathf.RoundToInt(_audio.MusicNoteHz));
                if (Elapsed() < ListenSeconds) return;
                _notesAfterListening = _audio.MusicNotesPlayed;
                GD.Print($"[MUSIC] {ListenSeconds:F0}s of corridor: notes {_notesAtStart} -> " +
                         $"{_notesAfterListening}, distinct pitches {_pitchesEarly.Count}, " +
                         $"intensity {_intensityEarly:F2}, starved {_timesStarved}x");
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
                _pitchesBoss.Add(Mathf.RoundToInt(_audio.MusicNoteHz));
                if (Elapsed() < ListenSeconds) return;

                GD.Print($"[MUSIC] {ListenSeconds:F0}s of boss room: distinct pitches " +
                         $"{_pitchesBoss.Count}, boss theme engaged {_bossThemeSeen}, " +
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
