using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the room's heat decides which channel an ordinary grunt
/// swings on.
///
/// This is the Crucible's central mechanic seen from the enemy's side. A
/// BasicMelee has one telegraph, StandardWhite, hard-coded in its base class --
/// so every channel it is ever seen on came from the heat, which makes it the
/// cleanest possible witness for the promotion rule.
///
/// THREE phases, because the ladder has three landings and the middle one is
/// the easy thing to get wrong:
///   cold (10)       -> StandardWhite, the type's own choice, unpromoted
///   Molten (75)     -> CounterGold, promoted by ONE step
///   Flashover (100) -> UnparryableRed, forced, not stepped
///
/// Note the middle line. Molten promotes by a single channel, so a white attack
/// becomes GOLD there and not red -- red from a plain grunt is the flashover's
/// doing. A check written to expect red at 75 would have been asserting a rule
/// the design does not contain, and would have been "fixed" by breaking the
/// ladder.
public partial class TelegraphPromotionTest : Node
{
    private int _f;
    private Node3D _player;
    private EnemyController _grunt;

    private int _phase;
    private int _phaseStart;
    private bool _windupSeen;

    private AttackTelegraphType _cold, _molten, _flash;
    private bool _gotCold, _gotMolten, _gotFlash;

    public override void _Process(double delta)
    {
        _f++;
        var heat = HeatManager.Instance;
        if (heat == null) { Done(false, "no heat manager"); return; }

        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) { if (_f > 180) Done(false, "no player"); return; }

        if (_f == 60)
        {
            var scene = GD.Load<PackedScene>("res://scenes/enemies/BasicMelee.tscn");
            if (scene == null) { Done(false, "no BasicMelee scene"); return; }
            _grunt = scene.Instantiate<EnemyController>();
            GetTree().Root.AddChild(_grunt);
            _grunt.GlobalPosition = _player.GlobalPosition + new Vector3(1.1f, 0f, 0f);
            _grunt.SetPatrolPoints(_grunt.GlobalPosition, _grunt.GlobalPosition);
            BeginPhase(0, heat, 10f);
            return;
        }

        if (_grunt == null || _f < 70) return;

        // The enemy is kept in reach: the player is a real controller and will
        // drift, and a grunt that loses sight stops attacking and the phase
        // times out on a fact about pathing rather than about telegraphs.
        _grunt.GlobalPosition = _player.GlobalPosition + new Vector3(1.1f, 0f, 0f);

        // Sampled on the RISING edge of a wind-up that began inside this phase.
        // Reading CurrentTelegraph at any old moment returns whatever the last
        // attack chose, which for the first frames of a phase is the previous
        // phase's answer.
        if (_grunt.IsWindingUp && !_windupSeen && _f > _phaseStart + 12)
        {
            _windupSeen = true;
            Record(_grunt.CurrentTelegraph);

            if (_phase == 0) { BeginPhase(1, heat, 75f); return; }
            if (_phase == 1) { BeginPhase(2, heat, heat.MaxHeat); return; }
            Report();
            return;
        }

        if (!_grunt.IsWindingUp) _windupSeen = false;

        if (_f > _phaseStart + 600)
            Done(false, $"phase {_phase} saw no wind-up in 10s");
    }

    private void BeginPhase(int phase, HeatManager heat, float toHeat)
    {
        _phase = phase;
        _phaseStart = _f;
        _windupSeen = true;
        heat.DebugForceActivate(toHeat);
        heat.DebugSetRoom(DungeonRoomBuilder.CrucibleFirstRoom);
    }

    private void Record(AttackTelegraphType t)
    {
        switch (_phase)
        {
            case 0: _cold = t; _gotCold = true; break;
            case 1: _molten = t; _gotMolten = true; break;
            default: _flash = t; _gotFlash = true; break;
        }
    }

    private void Report()
    {
        GD.Print($"[PROMOTION] heat 10  -> {_cold}   (expected StandardWhite)");
        GD.Print($"[PROMOTION] heat 75  -> {_molten} (expected CounterGold: Molten promotes by one channel)");
        GD.Print($"[PROMOTION] flashover -> {_flash}  (expected UnparryableRed: forced, not stepped)");

        bool ok = _gotCold && _gotMolten && _gotFlash
               && _cold == AttackTelegraphType.StandardWhite
               && _molten == AttackTelegraphType.CounterGold
               && _flash == AttackTelegraphType.UnparryableRed;

        Done(ok, ok ? "a grunt's white strike becomes gold at Molten and red in a flashover" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[PROMOTION] RESULT: PASS ({why})" : "[PROMOTION] RESULT: FAIL");
        GetTree().Quit();
    }
}
