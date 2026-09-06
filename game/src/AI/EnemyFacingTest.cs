using Godot;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: an enemy faces the player it is attacking.
///
/// Reported as "enemies strike in the opposite direction". The mechanism is
/// narrower and nastier than that: damage is applied on RADIAL distance and a
/// bolt's direction is computed from the player's position, so the hit always
/// landed correctly. What was wrong was the MODEL. Facing was driven by
/// velocity, TickAttack zeroes velocity so the enemy commits in place, and a
/// sentry or a warlock holding its range never moves at all -- so both kept
/// whatever facing they last had.
///
/// Nothing in the suite could see it, because every assertion was about
/// damage. This one is about where the enemy is pointing, which is the only
/// information the player has to time a parry with.
public partial class EnemyFacingTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private Node3D _player;
    private EnemyController _melee;

    private int _signWhenPlayerRight;
    private int _signWhenPlayerLeft;
    private bool _turnedDuringWindup;
    private bool _everWoundUp;
    private int _stationarySignRight;
    private int _stationarySignLeft;
    private bool _stationaryMoved;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_room == null || _player == null || _room.IsRebuilding) return;

        // -- Part 1: a melee enemy, mid-windup, with the player behind it ----
        if (_f == 20)
        {
            _melee = FindFirst<BasicMelee>(_room);
            if (_melee == null) { Done(false, "no melee enemy in room 0"); return; }
            _player.GlobalPosition = _melee.GlobalPosition + new Vector3(1.4f, 0.2f, 0f);
            return;
        }

        if (_melee != null && _f > 20 && _f < 400)
        {
            // Wait for a wind-up, then move the player to the far side WHILE it
            // is committed. TickAttack has already zeroed its velocity, so
            // velocity-driven facing has nothing left to read: this is the
            // exact state the bug lived in.
            if (_melee.IsWindingUp && !_everWoundUp)
            {
                _everWoundUp = true;
                _signWhenPlayerRight = _melee.FacingSign;
                _player.GlobalPosition = _melee.GlobalPosition + new Vector3(-1.4f, 0.2f, 0f);
                GD.Print($"[FACE] melee winding up, player moved behind it: facing was {_signWhenPlayerRight}");
                return;
            }

            if (_everWoundUp && _signWhenPlayerLeft == 0 && _f > 22)
            {
                _signWhenPlayerLeft = _melee.FacingSign;
                _turnedDuringWindup = _melee.IsWindingUp || _melee.IsStriking;
                GD.Print($"[FACE] one frame later: facing {_signWhenPlayerLeft} " +
                         $"(still committed to the attack: {_turnedDuringWindup})");
            }
        }

        // -- Part 2: a ranged enemy that never moves ------------------------
        if (_f == 420) { _room.RebuildAs(1); return; }

        if (_f == 450)
        {
            var sentry = FindFirst<CrossbowSentry>(_room);
            if (sentry == null) { Done(false, "no sentry in room 1"); return; }
            _player.GlobalPosition = sentry.GlobalPosition + new Vector3(5f, 0.2f, 0f);
            return;
        }

        if (_f == 500)
        {
            var sentry = FindFirst<CrossbowSentry>(_room);
            _stationarySignRight = sentry.FacingSign;
            _player.GlobalPosition = sentry.GlobalPosition + new Vector3(-5f, 0.2f, 0f);
            return;
        }

        if (_f == 550)
        {
            var sentry = FindFirst<CrossbowSentry>(_room);
            _stationarySignLeft = sentry.FacingSign;
            _stationaryMoved = Mathf.Abs(sentry.Velocity.X) > 0.05f;
            GD.Print($"[FACE] stationary sentry: player right -> {_stationarySignRight}, " +
                     $"player left -> {_stationarySignLeft} (it moved: {_stationaryMoved})");

            bool ok = _everWoundUp
                   && _signWhenPlayerRight != 0
                   && _signWhenPlayerLeft != 0
                   && _signWhenPlayerRight != _signWhenPlayerLeft   // it turned
                   && _turnedDuringWindup                           // and did so mid-attack
                   && _stationarySignRight != _stationarySignLeft;  // and standing still

            Done(ok, ok ? "enemies turn to face the player mid-attack and while standing still" : "");
        }

        if (_f > 600) Done(false, "the melee enemy never wound up an attack");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[FACE] RESULT: PASS ({why})"
                    : $"[FACE] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
