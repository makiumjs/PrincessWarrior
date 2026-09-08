using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: the Forgemaster's door is the heat, not its health.
///
/// The fight has one rule and this is it. Below Molten the boss alternates
/// white and gold and a perfect parry opens it for a stagger; at Molten every
/// strike is unparryable and there is no opening at all, so a player who let
/// the room get away from them cannot win it back by fighting harder.
///
/// Both halves are asserted, and the order matters: SEALED first. A check that
/// only proved the parry works below the threshold would pass on a boss with no
/// gate whatsoever, which is the version this replaced.
public partial class ForgemasterGateTest : Node
{
    private int _f;
    private Node3D _player;
    private Forgemaster _boss;

    private int _phase;
    private int _phaseStart;
    private bool _windupSeen;

    private AttackTelegraphType _sealedTelegraph, _openTelegraph;
    private bool _staggeredWhileSealed, _staggeredWhileOpen;
    private bool _stillStaggeredAt1s, _recoveredBy2s;
    private int _parriedAt = -1;
    private bool _reported;

    public override void _Process(double delta)
    {
        _f++;
        var heat = HeatManager.Instance;
        if (heat == null) { Done(false, "no heat manager"); return; }

        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        if (_player == null) { if (_f > 180) Done(false, "no player"); return; }

        if (_f == 60)
        {
            var scene = GD.Load<PackedScene>("res://scenes/enemies/Forgemaster.tscn");
            if (scene == null) { Done(false, "no Forgemaster scene"); return; }
            _boss = scene.Instantiate<Forgemaster>();
            GetTree().Root.AddChild(_boss);
            _boss.GlobalPosition = _player.GlobalPosition + new Vector3(1.8f, 0f, 0f);
            _boss.SetPatrolPoints(_boss.GlobalPosition, _boss.GlobalPosition);
            BeginPhase(0, heat, 80f);   // sealed
            return;
        }

        if (_boss == null || _f < 75) return;
        _boss.GlobalPosition = _player.GlobalPosition + new Vector3(1.8f, 0f, 0f);

        // Phase 2 bookkeeping runs regardless of wind-ups: the stagger has to be
        // measured on a clock, not on the next attack.
        if (_phase == 1 && _parriedAt > 0)
        {
            if (_f == _parriedAt + 60) _stillStaggeredAt1s = _boss.State == EnemyController.EnemyState.Stagger;
            if (_f == _parriedAt + 120)
            {
                _recoveredBy2s = _boss.State != EnemyController.EnemyState.Stagger;
                Report();
                return;
            }
        }

        if (_boss.IsWindingUp && !_windupSeen && _f > _phaseStart + 12)
        {
            _windupSeen = true;

            if (_phase == 0)
            {
                _sealedTelegraph = _boss.CurrentTelegraph;
                EventBus.Instance?.EmitParried(true, _boss.GlobalPosition);
                _staggeredWhileSealed = _boss.State == EnemyController.EnemyState.Stagger;
                BeginPhase(1, heat, 30f);   // open
                return;
            }

            if (_phase == 1 && _parriedAt < 0)
            {
                _openTelegraph = _boss.CurrentTelegraph;
                EventBus.Instance?.EmitParried(true, _boss.GlobalPosition);
                _staggeredWhileOpen = _boss.State == EnemyController.EnemyState.Stagger;
                _parriedAt = _f;
            }
            return;
        }

        if (!_boss.IsWindingUp) _windupSeen = false;

        if (_f > _phaseStart + 900)
            Done(false, $"phase {_phase} saw no wind-up in 15s");
    }

    private void BeginPhase(int phase, HeatManager heat, float toHeat)
    {
        _phase = phase;
        _phaseStart = _f;
        _windupSeen = true;
        heat.DebugForceActivate(toHeat);
        heat.DebugSetRoom(DungeonRoomBuilder.CrucibleLastRoom);
    }

    private void Report()
    {
        if (_reported) return;
        _reported = true;

        GD.Print($"[GATE] heat 80: telegraph {_sealedTelegraph}, a perfect parry staggered it: {_staggeredWhileSealed}");
        GD.Print($"[GATE] heat 30: telegraph {_openTelegraph}, a perfect parry staggered it: {_staggeredWhileOpen}");
        GD.Print($"[GATE] still staggered 1.0s later: {_stillStaggeredAt1s}; recovered by 2.0s: {_recoveredBy2s} (StaggerDuration {_boss.StaggerDuration:0.00})");
        GD.Print($"[GATE] chip while sealed {_boss.MoltenChipDamage}, while open {_boss.TemperedChipDamage}");

        bool sealedIsRed = _sealedTelegraph == AttackTelegraphType.UnparryableRed;
        bool sealedRefusesParry = !_staggeredWhileSealed;
        bool openIsParryable = _openTelegraph != AttackTelegraphType.UnparryableRed;
        bool openAcceptsParry = _staggeredWhileOpen;
        bool staggerLastsAboutOnePointFive = _stillStaggeredAt1s && _recoveredBy2s;
        bool chipHalves = _boss.MoltenChipDamage < _boss.TemperedChipDamage;

        bool ok = sealedIsRed && sealedRefusesParry && openIsParryable
               && openAcceptsParry && staggerLastsAboutOnePointFive && chipHalves;

        Done(ok, ok ? "at Molten nothing opens it; below, a perfect parry buys a 1.5s window" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[GATE] RESULT: PASS ({why})" : "[GATE] RESULT: FAIL");
        GetTree().Quit();
    }
}
