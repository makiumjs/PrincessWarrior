using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: nothing may leave the X/Y movement plane.
///
/// This is the premise the whole game rests on — a 2.5D side-scroller is a 3D
/// world that has agreed to stay flat — and mutation testing found it entirely
/// unverified: stripping the Z constraint broke no check. Drift off the plane
/// does not crash anything; it quietly makes hitboxes miss, the camera frame
/// wrongly and platforms stop catching the player.
///
/// Exercised under load: movement, jumps, dashes and attacks all at once, plus
/// enemies doing their own thing.
public partial class PlaneLockTest : Node
{
    private const float Tolerance = 0.05f;

    private int _f;
    private Node3D _player;
    private float _worstPlayerZ;
    private float _worstEnemyZ;
    private int _enemiesSeen;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        if (_f == 10)
        {
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.DoubleJump);
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.WallJump);
            Input.ActionPress("move_right");
        }

        // Keep it busy: every system that writes to velocity gets a turn.
        if (_f % 24 == 0) Input.ActionPress("jump");
        if (_f % 24 == 4) Input.ActionRelease("jump");
        if (_f % 40 == 0) Input.ActionPress("dash");
        if (_f % 40 == 5) Input.ActionRelease("dash");
        if (_f % 30 == 0) Input.ActionPress("attack_light");
        if (_f % 30 == 6) Input.ActionRelease("attack_light");

        if (_f > 20)
        {
            _worstPlayerZ = Mathf.Max(_worstPlayerZ, Mathf.Abs(_player.GlobalPosition.Z));
            CheckEnemies(GetTree().Root);
        }

        if (_f == 600)
        {
            GD.Print($"[PLANE] worst |Z|: player {_worstPlayerZ:F4}, enemies {_worstEnemyZ:F4} ({_enemiesSeen} seen)");
            if (_enemiesSeen == 0)
                GD.Print("[PLANE] RESULT: FAIL (no enemies observed - only half the world was checked)");
            else
                GD.Print(_worstPlayerZ < Tolerance && _worstEnemyZ < Tolerance
                    ? "[PLANE] RESULT: PASS (player and enemies stayed on the movement plane)"
                    : "[PLANE] RESULT: FAIL (something drifted off the X/Y plane)");
            GetTree().Quit();
        }
    }

    private void CheckEnemies(Node from)
    {
        if (from is EnemyController e && !e.IsQueuedForDeletion())
        {
            _enemiesSeen++;
            _worstEnemyZ = Mathf.Max(_worstEnemyZ, Mathf.Abs(e.GlobalPosition.Z));
        }
        foreach (var c in from.GetChildren()) CheckEnemies(c);
    }
}
