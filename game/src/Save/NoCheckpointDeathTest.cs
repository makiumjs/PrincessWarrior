using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.Save;

/// TEST-SCENE ONLY: kills the player with NO checkpoint ever banked, and checks
/// the run can continue. If nothing brings the player back, the save is empty
/// and the player stays dead — a softlock, with no way out but restarting.
public partial class NoCheckpointDeathTest : Node
{
    private int _f;
    private Node3D _player;
    private bool _died;

    public override void _Ready()
    {
        // Wipe whatever the spawn checkpoint banked, so this really is the
        // "died before reaching any checkpoint" case.
        EventBus.Instance.PlayerDied += () => { _died = true; GD.Print($"[NOCP] died at frame {_f}"); };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) return;

        if (_f == 10)
        {
            var save = SaveManager.Instance.Current;
            save.PlayerPosition = Vector3.Zero;
            save.LastCheckpointId = "";
            GD.Print("[NOCP] cleared any banked checkpoint");
        }

        if (_f == 20 && _player is IDamageable d)
            d.TakeDamage(new DamageInfo { Amount = 9999, SourcePosition = _player.GlobalPosition });

        if (_f == 90)
        {
            var hp = (int)_player.Get("CurrentHealth");
            var state = _player.Get("CurrentState");
            GD.Print($"[NOCP] 70 frames after death: hp={hp} state={state} pos={_player.GlobalPosition}");
            GD.Print(_died && hp > 0
                ? "[NOCP] RESULT: PASS (the run recovers even with no checkpoint banked)"
                : "[NOCP] RESULT: FAIL (player is stuck dead - softlock)");
            GetTree().Quit();
        }
    }
}
