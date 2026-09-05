using Godot;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: an enemy that falls out of the level must be removed.
///
/// Found by mutation testing: disabling the enemy fall plane broke no check.
/// A chasing enemy deliberately follows the player off a ledge — that is the
/// aggressive read — so without this every pursuit that ends in a pit leaves a
/// live body falling forever under the map, accumulating for the whole session.
public partial class EnemyFallDeathTest : Node
{
    private int _f;
    private CharacterBody3D _victim;
    private float _lowestY = 999f;

    public override void _Process(double delta)
    {
        _f++;

        if (_f == 20)
        {
            _victim = FindEnemy(GetTree().Root);
            if (_victim == null)
            {
                GD.Print("[ENEMYFALL] RESULT: FAIL (no enemy in the room - nothing was tested)");
                GetTree().Quit();
                return;
            }
            // Drop it into open space, far below any platform.
            _victim.GlobalPosition = new Vector3(_victim.GlobalPosition.X, -6f, 0f);
            GD.Print($"[ENEMYFALL] dropped an enemy into the void at {_victim.GlobalPosition}");
        }

        if (_victim != null && IsInstanceValid(_victim) && !_victim.IsQueuedForDeletion())
            _lowestY = Mathf.Min(_lowestY, _victim.GlobalPosition.Y);

        if (_f == 300)
        {
            bool gone = _victim == null || !IsInstanceValid(_victim) || _victim.IsQueuedForDeletion();
            GD.Print($"[ENEMYFALL] removed={gone} lowest y observed={_lowestY:F1}");
            GD.Print(gone
                ? "[ENEMYFALL] RESULT: PASS (an enemy that falls out of the level is removed)"
                : $"[ENEMYFALL] RESULT: FAIL (still falling at y={_lowestY:F1} - bodies accumulate under the map)");
            GetTree().Quit();
        }
    }

    private static CharacterBody3D FindEnemy(Node from)
    {
        if (from is EnemyController e && !e.IsQueuedForDeletion()) return e;
        foreach (var c in from.GetChildren()) { var f = FindEnemy(c); if (f != null) return f; }
        return null;
    }
}
