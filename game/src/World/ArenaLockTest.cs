using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: an arena cannot be run past, and opens when it is cleared.
///
/// The Arena chunk exists to teach the charge attack, and it was the easiest
/// stretch of ground in the room to sprint across -- the widest flat run, with
/// the enemies standing in the middle of it. So the one chunk built around a
/// fight was the one you could most reliably ignore.
///
/// Two things are asserted, and the second is the one that matters: that the
/// barrier stops the player, and that it LIFTS. A gate that never opens is a
/// worse bug than no gate, and it is the failure a check written only around
/// "the player is blocked" would call a pass.
public partial class ArenaLockTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private CharacterBody3D _player;
    private ArenaGate _gate;

    private float _gateX;
    private int _enemiesAtStart = -1;
    private bool _blocked;
    private float _reachedX;
    private bool _openedAfterClearing;
    private bool _passedAfterOpening;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        _player ??= GetTree().GetFirstNodeInGroup("player") as CharacterBody3D;
        if (_room == null || _player == null || _room.IsRebuilding) return;

        // Asked, not remembered: the ability gate holds arenas back until the
        // charge attack is due, and this said room 2.
        if (_f == 10) { _room.RebuildAs(_room.FirstRoomWith(ChunkKind.Arena)); return; }

        if (_f == 30)
        {
            _gate = FindFirst<ArenaGate>(_room);
            if (_gate == null) { Done(false, "no arena gate in an arena room"); return; }
            _gateX = _gate.GlobalPosition.X;
            _enemiesAtStart = _gate.LiveEnemiesInSpan();
            GD.Print($"[ARENA] gate at x={_gateX:F1}, enemies inside it: {_enemiesAtStart}");
            if (_enemiesAtStart == 0) { Done(false, "the arena spawned empty, so the gate proves nothing"); return; }

            // Put the player just inside the arena and hold right, the way
            // someone who wants to skip the fight would.
            _player.GlobalPosition = new Vector3(_gateX - 6f, _gate.GlobalPosition.Y + 1.2f, 0f);
            Input.ActionPress("move_right");
            return;
        }

        if (_f > 30 && _f < 260)
        {
            _reachedX = Mathf.Max(_reachedX, _player.GlobalPosition.X);
            return;
        }

        if (_f == 260)
        {
            _blocked = _reachedX < _gateX;
            GD.Print($"[ARENA] running at it for 230 frames reached x={_reachedX:F1} " +
                     $"(gate at {_gateX:F1}) -> blocked={_blocked}");

            foreach (var n in _room.GetChildren())
                if (n is EnemyController e && e.State != EnemyController.EnemyState.Dead)
                    e.TakeDamage(new DamageInfo
                    {
                        Amount = 100000,
                        SourcePosition = e.GlobalPosition,
                        Knockback = Vector3.Zero,
                        IsCritical = false,
                    });
            return;
        }

        if (_f == 300)
        {
            _openedAfterClearing = !IsInstanceValid(_gate) || _gate.IsOpen;
            GD.Print($"[ARENA] after clearing it: gate gone or open = {_openedAfterClearing}");
            return;
        }

        if (_f == 420)
        {
            _passedAfterOpening = _player.GlobalPosition.X > _gateX + 1f;
            Input.ActionRelease("move_right");
            GD.Print($"[ARENA] still holding right: player is now at x={_player.GlobalPosition.X:F1}");

            bool ok = _enemiesAtStart > 0
                   && _blocked
                   && _openedAfterClearing
                   && _passedAfterOpening;

            Done(ok, ok ? "the arena holds the player until it is cleared, then lets them through" : "");
        }
    }

    private void Done(bool ok, string why)
    {
        Input.ActionRelease("move_right");
        GD.Print(ok ? $"[ARENA] RESULT: PASS ({why})"
                    : $"[ARENA] RESULT: FAIL{(why.Length > 0 ? " (" + why + ")" : "")}");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
