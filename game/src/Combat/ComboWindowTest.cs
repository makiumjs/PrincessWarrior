using System.Collections.Generic;
using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;

namespace LostCrownlike.Combat;

/// TEST-SCENE ONLY: verifies the combo window in both directions.
///
/// Chaining inside the window must escalate damage; letting the window lapse
/// must drop back to step 0. A combo that never resets is not a combo — it is
/// a counter that only goes up, and it would look correct in any single fight.
public partial class ComboWindowTest : Node
{
    private readonly List<int> _chained = new();
    private readonly List<int> _afterLapse = new();
    private int _f;
    private Node3D _player;
    private bool _lapsePhase;

    public override void _Ready()
    {
        EventBus.Instance.EnemyDamaged += (_, amount, _, _, _) =>
        {
            (_lapsePhase ? _afterLapse : _chained).Add(amount);
            GD.Print($"[COMBO] hit for {amount} (frame {_f}, phase {(_lapsePhase ? "after lapse" : "chained")})");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        // Keep a living target adjacent and faced, and keep it alive: the
        // damage escalation is what is under test, not the kill.
        var enemy = FindLivingEnemy(GetTree().Root);
        if (enemy != null)
        {
            _player.GlobalPosition = enemy.GlobalPosition - new Vector3(1.1f, 0f, 0f);
            Input.ActionPress("move_right");
        }
        else if (_f > 40 && (_chained.Count < 2 || _afterLapse.Count < 1))
        {
            // Only a problem if the data is still incomplete. Targets dying
            // AFTER both phases were measured is normal — the escalation is
            // what kills them.
            GD.Print("[COMBO] RESULT: FAIL (no living enemy left, and not enough hits measured)");
            GetTree().Quit();
            return;
        }

        // Phase 1: three light attacks in quick succession, inside the window.
        if (_f is 40 or 55 or 70) Input.ActionPress("attack_light");
        if (_f is 43 or 58 or 73) Input.ActionRelease("attack_light");

        // Phase 2: wait well past ComboWindowMs, then attack again.
        if (_f == 100) _lapsePhase = true;
        if (_f == 400) Input.ActionPress("attack_light");
        if (_f == 403) Input.ActionRelease("attack_light");

        if (_f == 500 || (_chained.Count >= 2 && _afterLapse.Count >= 1 && _f > 405))
        {
            GD.Print($"[COMBO] chained hits: [{string.Join(", ", _chained)}]");
            GD.Print($"[COMBO] hit after the window lapsed: [{string.Join(", ", _afterLapse)}]");

            bool escalates = _chained.Count >= 2 && _chained[^1] > _chained[0];
            bool resets = _afterLapse.Count >= 1 && _chained.Count >= 1
                          && _afterLapse[0] == _chained[0];

            GD.Print(escalates && resets
                ? "[COMBO] RESULT: PASS (chaining escalates, and the window lapsing resets it)"
                : $"[COMBO] RESULT: FAIL (escalates={escalates} resets={resets})");
            GetTree().Quit();
        }
    }

    private static EnemyController FindLivingEnemy(Node from)
    {
        if (from is EnemyController e && e.State != EnemyController.EnemyState.Dead && !e.IsQueuedForDeletion())
            return e;
        foreach (var c in from.GetChildren()) { var f = FindLivingEnemy(c); if (f != null) return f; }
        return null;
    }
}
