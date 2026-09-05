using Godot;

namespace LostCrownlike.World;

/// TEST-ONLY: measures the frame cost of a room rebuild, so the decision to
/// build async-preload machinery is based on a number rather than a hunch.
public partial class RebuildHitchTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private ulong _last;
    private double _worstBefore, _rebuildMs, _worstAfter;
    private int _rebuildFrame = -1;

    public override void _Process(double delta)
    {
        _f++;
        ulong now = Time.GetTicksUsec();
        double ms = _last == 0 ? 0 : (now - _last) / 1000.0;
        _last = now;

        _room ??= GetTree().CurrentScene?.GetNodeOrNull<DungeonRoomBuilder>("Room");
        if (_room == null) return;

        if (_f > 5 && _f < 60 && ms > _worstBefore) _worstBefore = ms;

        if (_f == 60) _room.RebuildAs(1);
        if (_f == 61) _rebuildMs = ms;
        if (_f > 62 && _f < 120 && ms > _worstAfter) _worstAfter = ms;

        if (_f == 120)
        {
            GD.Print($"[HITCH] worst frame before rebuild: {_worstBefore:F2}ms");
            GD.Print($"[HITCH] frame containing rebuild:   {_rebuildMs:F2}ms");
            GD.Print($"[HITCH] worst frame after rebuild:  {_worstAfter:F2}ms");
            GD.Print(_rebuildMs > _worstBefore * 2.5
                ? "[HITCH] VERDICT: rebuild is a real hitch, async preload is justified"
                : "[HITCH] VERDICT: rebuild costs no more than a normal frame, async preload would be machinery for a non-problem");
            GetTree().Quit();
        }
    }
}
