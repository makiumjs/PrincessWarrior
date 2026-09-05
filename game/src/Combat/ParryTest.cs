using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.Combat;

/// TEST-SCENE ONLY: the parry, and the difference between a parry and a good one.
///
/// Four claims, each of which can fail on its own:
///   1. a blow that lands with no guard up still hurts (the baseline, and the
///      thing that makes the rest meaningful),
///   2. a guard raised in time stops the damage entirely,
///   3. a guard raised at the last moment -- past the perfect window -- still
///      stops the damage but does NOT stagger the attacker,
///   4. a perfect parry staggers it.
///
/// Without (1) the whole test could pass on an enemy that never connects, and
/// without (3) "perfect" would be indistinguishable from "parried at all",
/// which is the entire mechanic.
public partial class ParryTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private CombatController _combat;
    private EnemyController _enemy;

    // Starts below zero so the first NextPhase() lands ON phase 0 -- the
    // unguarded baseline. Starting at 0 skipped it, and the baseline is what
    // makes the other two mean anything.
    private int _phase = -1;
    private int _phaseStart;
    private int _damageEvents;
    private int _parries, _perfects;
    private bool _staggeredOnPerfect;

    private int _dmgUnguarded = -1, _dmgLateGuard = -1, _dmgPerfect = -1;
    private bool _staggerLate, _staggerPerfect;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 20)
        {
            _combat = _player.GetNodeOrNull<CombatController>("CombatController");
            EventBus.Instance.PlayerDamaged += (_, _, _, _) => _damageEvents++;
            EventBus.Instance.Parried += (perfect, _) =>
            {
                _parries++;
                if (perfect) _perfects++;
            };
            NextPhase();
            return;
        }
        if (_combat == null) return;

        int t = _f - _phaseStart;

        // The enemy is placed in reach and left to swing on its own timing;
        // scripting the swing would test the test's idea of when a blow lands
        // rather than the game's.
        if (_enemy != null && IsInstanceValid(_enemy))
        {
            // Phase 1: guard early, well inside the perfect window.
            // Phase 2: guard late, after it has expired but before the blow.
            // Phase 0: no guard at all.
            if (_phase == 1 && _enemy.IsWindingUp && _enemy.WindupProgress > 0.85f)
                Press("parry");
            else if (_phase == 2 && _enemy.IsWindingUp && _enemy.WindupProgress is > 0.05f and < 0.15f)
                Press("parry");
            else Release("parry");

            if (_phase == 2 && _enemy.State == EnemyController.EnemyState.Stagger) _staggerLate = true;
            if (_phase == 1 && _enemy.State == EnemyController.EnemyState.Stagger) _staggerPerfect = true;
        }

        if (t == 260)
        {
            if (_phase == 0) _dmgUnguarded = _damageEvents;
            if (_phase == 2) _dmgLateGuard = _damageEvents;
            if (_phase == 1) _dmgPerfect = _damageEvents;
            NextPhase();
        }

        if (_phase > 2 && t == 20)
        {
            GD.Print($"[PARRY] no guard: {_dmgUnguarded} hits taken");
            GD.Print($"[PARRY] late guard: {_dmgLateGuard} hits taken, attacker staggered: {_staggerLate}");
            GD.Print($"[PARRY] perfect: {_dmgPerfect} hits taken, attacker staggered: {_staggerPerfect}");
            GD.Print($"[PARRY] parries {_parries}, of which perfect {_perfects}");

            bool ok = _dmgUnguarded > 0            // an unguarded blow hurts
                   && _dmgLateGuard == 0           // a late guard still saves you
                   && !_staggerLate                // but does not punish
                   && _dmgPerfect == 0             // a perfect guard saves you
                   && _staggerPerfect              // and punishes
                   && _perfects > 0;

            GD.Print(ok
                ? "[PARRY] RESULT: PASS (a guard stops the blow; only a perfect one staggers the attacker)"
                : "[PARRY] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private void NextPhase()
    {
        if (_enemy != null && IsInstanceValid(_enemy)) _enemy.QueueFree();
        Release("parry");
        _phase++;
        _phaseStart = _f;
        _damageEvents = 0;

        if (_phase > 2) return;

        _enemy = GD.Load<PackedScene>("res://scenes/enemies/BasicMelee.tscn")
                   .Instantiate<EnemyController>();
        _room.AddChild(_enemy);
        _enemy.GlobalPosition = _player.GlobalPosition + new Vector3(1.5f, 0f, 0f);
    }

    private bool _held;
    private void Press(string a) { if (!_held) { Input.ActionPress(a); _held = true; } }
    private void Release(string a) { if (_held) { Input.ActionRelease(a); _held = false; } }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
