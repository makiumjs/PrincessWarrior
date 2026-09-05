using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: parks the player next to an enemy and checks the enemy
/// actually lands a hit. If this fails the whole health/death/respawn loop is
/// decorative — nothing can hurt the player.
public partial class EnemyDamageTest : Node
{
    private int _f;
    private bool _damaged;
    private Node3D _player;
    private EnemyController _enemy;

    private static World.DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is World.DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }

    private static EnemyController FindEnemy(Node from)
    {
        if (from is EnemyController e && !e.IsQueuedForDeletion()) return e;
        foreach (var c in from.GetChildren())
        {
            var found = FindEnemy(c);
            if (found != null) return found;
        }
        return null;
    }

    public override void _Ready()
    {
        EventBus.Instance.PlayerDamaged += (amount, _, _, _) =>
        {
            if (_damaged) return;
            _damaged = true;
            GD.Print($"[ENEMYDMG] player took {amount} at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        // A rebuild now runs one step per frame instead of all on the frame the
        // player crosses a door, so for a few frames the room exists but its
        // enemies do not yet. This test declares failure when it cannot find an
        // enemy, and inside that window it could not -- it read a half-built
        // room and called the content missing.
        var room = FindRoom(GetTree().Root);
        if (room != null && room.IsRebuilding) return;

        // Re-acquire every frame rather than caching: spikes damage enemies
        // too, so the target can die and be freed mid-test, and a stale
        // reference throws on every subsequent frame — the test then never
        // reaches its own verdict.
        if (_enemy == null || !IsInstanceValid(_enemy) || _enemy.IsQueuedForDeletion())
            _enemy = FindEnemy(GetTree().Root);
        if (_enemy == null)
        {
            if (_f > 20) { GD.Print("[ENEMYDMG] RESULT: FAIL (no enemy found in room)"); GetTree().Quit(); }
            return;
        }

        // Park the player right next to the enemy every frame so the encounter
        // definitely happens; what is under test is the enemy's attack, not
        // whether a bot can walk there.
        if (_f is > 10 and < 400)
            _player.GlobalPosition = _enemy.GlobalPosition + new Vector3(1.0f, 0f, 0f);

        if (_f == 400)
        {
            GD.Print($"[ENEMYDMG] enemy at {_enemy.GlobalPosition}, state={_enemy.State}");
            GD.Print(_damaged ? "[ENEMYDMG] RESULT: PASS (enemy can hurt the player)"
                              : "[ENEMYDMG] RESULT: FAIL (enemy never landed a hit)");
            GetTree().Quit();
        }
    }
}
