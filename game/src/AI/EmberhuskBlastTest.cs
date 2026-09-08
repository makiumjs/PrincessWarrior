using Godot;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.AI;

/// TEST-SCENE ONLY: where you kill an Emberhusk is the whole of the encounter.
///
/// Two kills, and the SECOND one is what makes the first mean anything. A husk
/// killed at arm's length must cost the player health; the same husk killed
/// across the room must not. Without the control, "the detonation dealt damage"
/// would pass on a blast with infinite radius, which is not area denial -- it
/// is a room-wide tax with a fuse.
///
/// The pool is asserted the way this project learned to assert cleanup, after
/// a bolt check reported "peak bolts in flight: 0" and proved that zero bolts
/// had been cleaned up: the fire is confirmed to EXIST at mid-life before it is
/// confirmed to be gone. A pool that was never spawned would otherwise satisfy
/// "no pool remains" perfectly.
public partial class EmberhuskBlastTest : Node
{
    private int _f;
    private PlayerController _player;
    private Emberhusk _near, _far;

    private int _healthBeforeNear = -1, _healthAfterNear = -1;
    private int _healthBeforeFar = -1, _healthAfterFar = -1;
    private bool _poolAtMidLife, _poolAfterLifetime = true;
    private float _heatBeforeNear = -1f, _heatAfterNear = -1f;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player == null) { if (_f > 180) Done(false, "no player"); return; }

        // Armed so the detonation's heat contribution has somewhere to land.
        if (_f == 60) { World.HeatManager.Instance?.DebugForceActivate(40f); return; }

        // --- the near kill -------------------------------------------------
        if (_f == 70) { _near = Spawn(1.5f); return; }

        if (_f == 80)
        {
            if (_near == null) { Done(false, "no husk"); return; }
            _healthBeforeNear = _player.CurrentHealth;
            _heatBeforeNear = World.HeatManager.Instance?.Heat ?? -1f;
            Kill(_near);
            return;
        }

        // 0.35s of fuse is 21 frames at 60; sampled well past it.
        if (_f == 120)
        {
            _healthAfterNear = _player.CurrentHealth;
            _heatAfterNear = World.HeatManager.Instance?.Heat ?? -1f;
            return;
        }

        // Mid-life of a 3.5s pool lit at frame ~61: around frame 165.
        if (_f == 205) { _poolAtMidLife = AnyPool(); return; }

        // Comfortably past 3.5s from the detonation.
        if (_f == 340) { _poolAfterLifetime = AnyPool(); return; }

        // --- the control: the same death, out of reach ---------------------
        if (_f == 350) { _far = Spawn(12f); return; }

        if (_f == 360)
        {
            if (_far == null) { Done(false, "no control husk"); return; }
            _healthBeforeFar = _player.CurrentHealth;
            Kill(_far);
            return;
        }

        if (_f == 410)
        {
            _healthAfterFar = _player.CurrentHealth;
            Report();
        }
    }

    private Emberhusk Spawn(float offsetX)
    {
        var scene = GD.Load<PackedScene>("res://scenes/enemies/Emberhusk.tscn");
        if (scene == null) return null;
        var husk = scene.Instantiate<Emberhusk>();
        GetTree().Root.AddChild(husk);
        husk.GlobalPosition = _player.GlobalPosition + new Vector3(offsetX, 0f, 0f);
        husk.SetPatrolPoints(husk.GlobalPosition, husk.GlobalPosition);
        return husk;
    }

    /// Killed outright rather than whittled: the claim is about the death, and
    /// a husk staggering through four blows would drift out of the position
    /// the measurement depends on.
    private void Kill(Emberhusk husk) => husk.TakeDamage(new DamageInfo
    {
        Amount = husk.MaxHealth * 4,
        SourcePosition = husk.GlobalPosition,
        Knockback = Vector3.Zero,
        IsCritical = false,
        Telegraph = AttackTelegraphType.StandardWhite,
    });

    private bool AnyPool() => FindPool(GetTree().Root) != null;

    private static EmberPool FindPool(Node from)
    {
        if (from is EmberPool p && !p.IsQueuedForDeletion()) return p;
        foreach (var c in from.GetChildren()) { var f = FindPool(c); if (f != null) return f; }
        return null;
    }

    private void Report()
    {
        int nearCost = _healthBeforeNear - _healthAfterNear;
        int farCost = _healthBeforeFar - _healthAfterFar;
        float heatAdded = _heatAfterNear - _heatBeforeNear;

        GD.Print($"[BLAST] killed at 1.5m: player lost {nearCost} health");
        GD.Print($"[BLAST] killed at 12m (control): player lost {farCost} health");
        GD.Print($"[BLAST] heat {_heatBeforeNear:0.0} -> {_heatAfterNear:0.0} across the detonation (+{heatAdded:0.0})");
        GD.Print($"[BLAST] pool burning at mid-life: {_poolAtMidLife}; pool gone after its lifetime: {!_poolAfterLifetime}");

        bool blastHurts = nearCost > 0;
        bool distanceSaves = farCost <= 0;
        bool poolExisted = _poolAtMidLife;          // asserted BEFORE the cleanup claim
        bool poolCleanedUp = !_poolAfterLifetime;
        bool heatPaid = heatAdded > 0f;

        bool ok = blastHurts && distanceSaves && poolExisted && poolCleanedUp && heatPaid;
        Done(ok, ok ? $"a husk killed close costs {nearCost} health and one killed far costs none; the fire burns then clears" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[BLAST] RESULT: PASS ({why})" : "[BLAST] RESULT: FAIL");
        GetTree().Quit();
    }
}
