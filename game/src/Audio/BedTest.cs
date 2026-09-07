using Godot;
using System.Collections.Generic;
using System.Linq;
using LostCrownlike.World;

namespace LostCrownlike.Audio;

/// TEST-SCENE ONLY: the recorded atmosphere follows the run, the boss outranks
/// the act, and every layer the mapping can name is actually reached.
///
/// It walks the whole run rather than sampling three rooms, because the claim
/// has two halves and only the walk covers the second. The first is that the
/// mapping is right: acts by position, boss over act. The second is that
/// nothing here is decorative -- this project has closed five cases of a
/// declared thing with no path to it, and a layer that no room shape ever picks
/// would be the sixth.
///
/// It also resets the save first. The run is walked from room 0, and a leftover
/// save from an earlier check boots the game into the boss room -- which is how
/// the boss theme was first caught outliving its boss.
public partial class BedTest : Node
{
    private const int Settle = 20;

    private int _f, _room = -1, _frames;
    private bool _started, _walked;
    private string _afterBoss = "";
    private AudioManager _audio;
    private DungeonRoomBuilder _rooms;
    private readonly List<string> _beds = new();
    private readonly List<string> _layers = new();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath("user://save.cfg"));
        LostCrownlike.Save.SaveManager.Instance?.ResetSave();
    }

    public override void _Process(double delta)
    {
        _f++;
        _audio ??= FindFirst<AudioManager>(GetTree().Root);
        _rooms ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_audio == null || _rooms == null)
        {
            if (_f > 120) Done(false, "no audio manager, or no room builder");
            return;
        }
        if (_rooms.IsRebuilding) return;
        _frames++;

        if (!_started) { _started = true; _room = 0; _frames = 0; _rooms.RebuildAs(0); return; }
        if (_frames < Settle) return;

        _beds.Add(_audio.CurrentBedName);
        _layers.Add(_audio.CurrentLayerName);

        if (_room + 1 < _rooms.RunLength)
        {
            _room++; _frames = 0; _rooms.RebuildAs(_room);
            return;
        }

        // Back to room 0 after the boss room, which is what dying to the Warden
        // does: the checkpoint is behind you and the room rebuilds. The Warden
        // is freed rather than killed, so nothing said the fight was over and
        // the boss theme kept playing three rooms back. This is the half of the
        // claim the forward walk cannot reach.
        if (!_walked)
        {
            _walked = true; _frames = 0; _rooms.RebuildAs(0);
            return;
        }
        _afterBoss = _beds[_beds.Count - 1];

        for (int i = 0; i < _beds.Count; i++)
            GD.Print($"[BEDS] room {i}: bed {_beds[i]}  layer {_layers[i]}");

        bool audible = new[] { "BedA", "BedB", "LayerA", "LayerB" }
            .Select(n => _audio.GetNodeOrNull<AudioStreamPlayer>(n))
            .Any(v => v is { Playing: true } && v.VolumeDb > -40f);

        int last = _rooms.RunLength - 1;   // _beds ha una voce in piu': il ritorno
        // Act II is read one room PAST the halfway mark, and that is not a
        // dodge. The halfway room holds the Sentinel -- an armoured, sealed
        // fight -- and a sealed fight takes the boss theme, which is the
        // behaviour this project wants: the atmosphere marks the punctuation.
        // Sampling act II on top of it would be asking the wrong room.
        bool acts = _beds[0] == "act1_forgotten_crypts"
                 && _beds[_rooms.RunLength / 2] == "boss_theme"
                 && _beds[_rooms.RunLength / 2 + 1] == "act2_sunken_catacombs"
                 && _beds[(int)(_rooms.RunLength * 0.75f)] == "act3_wardens_sanctum"
                 && _beds[last] == "boss_theme";
        var distinctLayers = _layers.Where(s => s.Length > 0).Distinct().Count();

        GD.Print($"[BEDS] acts in order: {acts}   distinct layers across the run: " +
                 $"{distinctLayers} of 3   a voice is audible: {audible}");
        GD.Print($"[BEDS] back in room 0 after the boss room: {_afterBoss}");

        bool released = _afterBoss == "act1_forgotten_crypts";
        bool ok = acts && distinctLayers == 3 && audible && released;
        Done(ok, ok ? "each act has its bed, the boss takes it and gives it back, and all three room shapes are reached" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[BEDS] RESULT: PASS ({why})" : "[BEDS] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
