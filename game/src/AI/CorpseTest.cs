using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: do the dead go away?
///
/// EnemyController.Die() sets the state and leaves the body in the scene so the
/// death animation can play and the corpse can slide to a stop. Nothing frees
/// it afterwards, and a room is not rebuilt while you fight in it, so this asks
/// whether bodies accumulate over a long fight.
public partial class CorpseTest : Node
{
    private const int Kills = 12;

    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private int _spawned;
    private int _peakCorpses;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f > 30 && _f % 20 == 0 && _spawned < Kills)
        {
            _spawned++;
            var e = GD.Load<PackedScene>("res://scenes/enemies/BasicMelee.tscn")
                      .Instantiate<EnemyController>();
            _room.AddChild(e);
            e.GlobalPosition = _player.GlobalPosition + new Vector3(4f + _spawned * 0.4f, 0f, 0f);
            // Killed outright rather than fought: this measures cleanup, not
            // whether the player can win a fight.
            e.TakeDamage(new DamageInfo { Amount = 9999, SourcePosition = _player.GlobalPosition });
        }

        _peakCorpses = Mathf.Max(_peakCorpses, CountDead(_room));

        if (_f == 700)
        {
            int left = CountDead(_room);
            GD.Print($"[CORPSE] killed {_spawned}, corpses peaked at {_peakCorpses}, still present: {left}");
            GD.Print(left == 0
                ? "[CORPSE] RESULT: PASS (bodies are cleaned up after they have fallen)"
                : "[CORPSE] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static int CountDead(Node from)
    {
        int n = from is EnemyController { State: EnemyController.EnemyState.Dead } ? 1 : 0;
        foreach (var c in from.GetChildren()) n += CountDead(c);
        return n;
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
