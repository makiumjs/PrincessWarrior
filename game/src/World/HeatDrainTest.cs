using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: a perfect parry takes exactly twelve off the bar.
///
/// The drain is the other half of the curve, and the half the player controls.
/// Asserted as an EXACT figure rather than "the heat went down": a drain of one
/// would also go down, and would make the branch unwinnable while every check
/// stayed green.
///
/// Both readings are taken inside a single _Process call. The passive gain runs
/// in _PhysicsProcess, so nothing accrues between them and the difference is
/// the parry and only the parry -- measuring across frames would fold 2.8/s of
/// drift into a figure claiming to be exact.
public partial class HeatDrainTest : Node
{
    private int _f;
    private bool _armed;

    public override void _Process(double delta)
    {
        _f++;
        var heat = HeatManager.Instance;
        if (heat == null) { Done(false, "no heat manager"); return; }

        if (!_armed)
        {
            // Past room 0's RoomEntered, which would switch the manager off.
            if (_f < 60) return;

            // Mid-bar on purpose: a drain measured at the bottom would be
            // clamped at zero and would report as correct for any value.
            heat.DebugForceActivate(60f);
            heat.DebugSetRoom(DungeonRoomBuilder.CrucibleFirstRoom);
            _armed = true;
            return;
        }

        if (_f < 80) return;

        float beforePerfect = heat.Heat;
        EventBus.Instance?.EmitParried(true, Vector3.Zero);
        float afterPerfect = heat.Heat;

        float beforeBlocked = heat.Heat;
        EventBus.Instance?.EmitParried(false, Vector3.Zero);
        float afterBlocked = heat.Heat;

        float perfectDrain = beforePerfect - afterPerfect;
        float blockedDrain = beforeBlocked - afterBlocked;

        GD.Print($"[HEATDRAIN] perfect parry: {beforePerfect:0.00} -> {afterPerfect:0.00} (drained {perfectDrain:0.00}, expected {heat.HeatPerPerfectParry:0.00})");
        GD.Print($"[HEATDRAIN] blocked parry: {beforeBlocked:0.00} -> {afterBlocked:0.00} (drained {blockedDrain:0.00}, expected {heat.HeatPerBlockedParry:0.00})");

        // Both channels, because the two are easy to wire to the same constant
        // and the design says they differ by exactly half.
        bool perfectExact = Mathf.Abs(perfectDrain - heat.HeatPerPerfectParry) < 0.01f;
        bool blockedExact = Mathf.Abs(blockedDrain - heat.HeatPerBlockedParry) < 0.01f;
        bool theyDiffer = perfectDrain > blockedDrain;

        bool ok = perfectExact && blockedExact && theyDiffer;
        Done(ok, ok ? $"a perfect parry drains {perfectDrain:0} and a blocked one {blockedDrain:0}" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[HEATDRAIN] RESULT: PASS ({why})" : "[HEATDRAIN] RESULT: FAIL");
        GetTree().Quit();
    }
}
