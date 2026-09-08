using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: a player who does nothing burns, and burns on schedule.
///
/// The branch's whole premise is that the room has a clock the player winds
/// back by fighting well. If the clock is wrong the design is wrong, and no
/// amount of correct combat code fixes it.
///
/// Deliberately measured in ISOLATION: the manager is armed and the timer is
/// watched, with no room rebuilt around it. The claim under test is the CURVE,
/// not the room, and a room full of Emberwrights adds 2.4/s apiece -- which is
/// a different claim with a different number. The check asserts there are zero
/// live heat sources, so the figure it reports is attributable to the base rate
/// and nothing else. That control is the lesson this project already paid for
/// once, when a threat measurement blamed spike traps for enemy damage.
///
/// From the entry heat of 25, at room 4's 2.80/s, the bar tops out at 26.8s.
public partial class HeatCurveTest : Node
{
    private const float ExpectedSeconds = 26.8f;
    private const float ToleranceSeconds = 1.5f;
    private const float TimeoutSeconds = 45f;

    /// Nothing is armed before this frame. Main builds room 0 at startup and
    /// emits RoomEntered(0) when it finishes, and index 0 is outside the
    /// branch -- so a manager armed on frame one is switched back off a few
    /// frames later and the timer never starts. Learned the hard way; every
    /// Crucible check in this suite waits the same beat.
    private const int ArmAtFrame = 60;

    private int _f;
    private float _elapsed;
    private float _flashoverAt = -1f;
    private int _sourcesAtStart = -1;
    private bool _armed;
    private bool _subscribed;

    public override void _Ready()
    {
        if (EventBus.Instance == null) return;
        EventBus.Instance.Flashover += OnFlashover;
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        if (EventBus.Instance != null) EventBus.Instance.Flashover -= OnFlashover;
        _subscribed = false;
    }

    private void OnFlashover(Vector3 at)
    {
        if (_flashoverAt < 0f) _flashoverAt = _elapsed;
    }

    public override void _Process(double delta)
    {
        _f++;
        var heat = HeatManager.Instance;
        if (heat == null) { Done(false, "no heat manager"); return; }

        if (!_armed)
        {
            if (_f < ArmAtFrame) return;

            heat.DebugForceActivate(heat.EntryHeat);
            heat.DebugSetRoom(DungeonRoomBuilder.CrucibleFirstRoom);
            _sourcesAtStart = heat.LiveHeatSourceCount();
            _armed = true;
            return;
        }

        _elapsed += (float)delta;

        if (_flashoverAt < 0f)
        {
            if (_elapsed > TimeoutSeconds) Done(false, $"no flashover in {TimeoutSeconds:0}s (heat sat at {heat.Heat:0.0})");
            return;
        }

        float error = Mathf.Abs(_flashoverAt - ExpectedSeconds);
        GD.Print($"[HEATCURVE] entry {heat.EntryHeat:0} + {heat.HeatGainRoom4:0.00}/s -> flashover at {_flashoverAt:0.00}s (expected {ExpectedSeconds:0.0} +/- {ToleranceSeconds:0.0})");
        GD.Print($"[HEATCURVE] live heat sources during the measurement: {_sourcesAtStart}");
        GD.Print($"[HEATCURVE] heat after the flashover: {heat.Heat:0.0} (reset target {heat.FlashoverResetTo:0})");

        bool onSchedule = error <= ToleranceSeconds;
        bool attributable = _sourcesAtStart == 0;
        bool resetNotZeroed = heat.Heat > 1f;   // 60, not a clean bar

        bool ok = onSchedule && attributable && resetNotZeroed;
        Done(ok, ok ? $"a passive player flashes over at {_flashoverAt:0.0}s, from the base rate alone" : "");
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[HEATCURVE] RESULT: PASS ({why})" : "[HEATCURVE] RESULT: FAIL");
        GetTree().Quit();
    }
}
