using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the halfway room holds an armoured fight that seals its own
/// exit, and it is not the Warden.
///
/// The parry is this game's signature move and armour is the only thing that
/// REQUIRES it. Until the Sentinel, the player met that demand once -- in the
/// last room, twelve minutes in -- so the mechanic the finale rests on was
/// taught by the finale. It is also the run's only punctuation: twelve minutes
/// with one event is a long flat line.
///
/// What is asserted is what makes it a lesson rather than a bigger grunt: the
/// armour turns a full blow into chip damage, and the door does not open while
/// it lives. A check that only found "an enemy in room 5" would pass on a
/// reskinned melee grunt, which is exactly what this must not be.
public partial class SentinelTest : Node
{
    private int _f;
    private DungeonRoomBuilder _rooms;
    private EnemyController _sentinel;
    private bool _sealedWhileAlive, _isSentinelType;
    private int _healthBefore = -1, _healthAfterFullBlow = -1;

    public override void _Process(double delta)
    {
        _f++;
        _rooms ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_rooms == null) { if (_f > 120) Done(false, "no room builder"); return; }

        if (_f == 10) { _rooms.RebuildAs(_rooms.RunLength / 2); return; }
        if (_rooms.IsRebuilding) return;

        if (_f == 40)
        {
            foreach (var n in GetTree().GetNodesInGroup("boss"))
                if (n is EnemyController e) _sentinel = e;

            if (_sentinel == null) { Done(false, "nothing stands in the halfway room"); return; }
            _isSentinelType = _sentinel.GetType() == typeof(Sentinel);

            var exit = FindFirst<RoomExitTrigger>(_rooms);
            _sealedWhileAlive = exit != null && exit.SealedUntilBossDies;
            _healthBefore = _sentinel.CurrentHealth;

            // A blow that would kill a grunt outright. Armour is the claim, so
            // the number has to be one that only armour survives.
            _sentinel.TakeDamage(new DamageInfo
            {
                Amount = 55,
                SourcePosition = _sentinel.GlobalPosition + new Vector3(-2f, 0f, 0f),
                Knockback = Vector3.Zero,
                IsCritical = false,
            });
            return;
        }

        if (_f == 60)
        {
            _healthAfterFullBlow = _sentinel.CurrentHealth;
            int taken = _healthBefore - _healthAfterFullBlow;

            GD.Print($"[SENTINEL] type is Sentinel, not Warden: {_isSentinelType}");
            GD.Print($"[SENTINEL] health {_healthBefore} -> {_healthAfterFullBlow} from a 55-damage blow ({taken} taken)");
            GD.Print($"[SENTINEL] the room's exit is sealed while it lives: {_sealedWhileAlive}");

            bool ok = _isSentinelType
                   && _sealedWhileAlive
                   && _healthBefore is > 0 and < 120   // lesser than the Warden's
                   && taken > 0 && taken <= 5;         // chipped, not ignored, not landed
            Done(ok, ok ? "an armoured fight at the halfway mark, sealed in, and not the Warden" : "");
        }
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[SENTINEL] RESULT: PASS ({why})" : "[SENTINEL] RESULT: FAIL");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
