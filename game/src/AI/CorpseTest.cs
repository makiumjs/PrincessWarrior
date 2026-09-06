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

    /// Linger plus sink, with a frame of slack. Kept next to the assertion
    /// rather than read off the enemy, so the check states the contract it is
    /// holding the game to instead of asking the game what it does.
    private const float Budget = 3.5f + 0.9f + 0.05f;

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
            float oldest = OldestCorpse(_room);
            GD.Print($"[CORPSE] killed {_spawned}, corpses peaked at {_peakCorpses}, still present: {left}, " +
                     $"oldest has been dead {oldest:F2}s (budget {Budget:F2}s)");
            ReportSurvivors(_room);

            // Age, not presence. "Zero bodies" was the right assertion while
            // the only deaths in the room were the twelve this check causes;
            // once the rooms got long enough to contain their own casualties it
            // started failing on a corpse that was 3.23s into a 4.40s cleanup
            // -- working exactly as designed. What a leak actually looks like
            // is a body OLDER than its own budget, and that is now what fails.
            bool ok = left == 0 || oldest <= Budget;
            GD.Print(ok
                ? $"[CORPSE] RESULT: PASS (nothing is left beyond its {Budget:F1}s cleanup budget)"
                : "[CORPSE] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// What is left, and for how long. A body that died two seconds ago is not
    /// a leak; one that has been dead for twenty is.
    private static void ReportSurvivors(Node from)
    {
        if (from is EnemyController { State: EnemyController.EnemyState.Dead } e)
            GD.Print($"[CORPSE]   survivor {e.Name} ({e.GetType().Name}) at " +
                     $"({e.GlobalPosition.X:F1}, {e.GlobalPosition.Y:F1}) " +
                     $"deadFor={e.DeadForSeconds:F2}s queued={e.IsQueuedForDeletion()}");
        foreach (var c in from.GetChildren()) ReportSurvivors(c);
    }

    private static float OldestCorpse(Node from)
    {
        float oldest = from is EnemyController { State: EnemyController.EnemyState.Dead } e
            ? e.DeadForSeconds : 0f;
        foreach (var c in from.GetChildren()) oldest = Mathf.Max(oldest, OldestCorpse(c));
        return oldest;
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
