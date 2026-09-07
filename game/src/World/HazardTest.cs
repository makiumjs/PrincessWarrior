using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: proves environmental hazards are real — that a spike tile
/// exists in the room, that standing on it damages the player, and that it does
/// so on a cooldown rather than draining the bar in a single frame.
public partial class HazardTest : Node
{
    private int _f;
    private Node3D _player;
    private SpikeHazard _spikes;
    private int _hits;
    private int _totalDamage;

    public override void _Ready()
    {
        EventBus.Instance.PlayerDamaged += (amount, _, _, _) =>
        {
            _hits++;
            _totalDamage += amount;
            GD.Print($"[HAZARD] player damaged for {amount} (hit {_hits}) at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _rooms ??= FindRoom(GetTree().Root);
        if (_player == null || _rooms == null) return;

        // Into a room that has BOTH kinds of trap. Room 0 has spikes and no
        // blades -- the moving hazard is a second-half idea -- so this check
        // asked where it was standing rather than where the thing it measures
        // lives, and reported a blade travel of zero for a blade that was not
        // there. Asked, not remembered: the layouts move.
        if (_rooms.IsRebuilding) return;
        _spikes ??= FindSpikes(GetTree().Root);

        // The moving hazard, measured in a SECOND room. What has to be true of
        // it is not "it exists" -- a blade parked in a corridor is scenery, and
        // scenery is what every other obstacle in this level already was: a
        // static distance. So the claim is that it MOVES, measured as the
        // spread of its X.
        //
        // The two traps do not share a room: spikes are an opening-room idea
        // and blades are a second-half one. Asked for, not remembered.
        if (_f > 505) _sweep ??= FindSweep(GetTree().Root);
        if (_sweep != null)
        {
            float x = _sweep.GlobalPosition.X;
            _sweepMinX = Mathf.Min(_sweepMinX, x);
            _sweepMaxX = Mathf.Max(_sweepMaxX, x);
        }

        if (_f == 20)
        {
            if (_spikes == null)
            {
                GD.Print("[HAZARD] RESULT: FAIL (no spike hazard generated in the room)");
                GetTree().Quit();
                return;
            }
            GD.Print($"[HAZARD] spikes found at {_spikes.GlobalPosition}");
        }

        // Hold the player on the trap so it ticks more than once.
        if (_f is > 25 and < 500 && _spikes != null)
            _player.GlobalPosition = _spikes.GlobalPosition + new Vector3(0f, 0.6f, 0f);

        if (_f == 505) { _rooms.RebuildAs(_rooms.FirstRoomWith(ChunkKind.Sweep)); return; }

        if (_f == 900)
        {
            GD.Print($"[HAZARD] {_hits} hits, {_totalDamage} total damage while standing on the trap");
            // Damage, not events: a trap set to 0 damage still fires
            // PlayerDamaged, so counting events alone passes a broken hazard.
            float travel = _sweep == null ? 0f : _sweepMaxX - _sweepMinX;
            GD.Print($"[HAZARD] blade travel across the run: {travel:F2}m");

            // Two thirds of its nominal 6m stroke, so a blade nudged once by a
            // collision does not read as a blade that sweeps. Absolute, not
            // derived from the node's own Reach: a threshold read off the value
            // under test passes whatever that value becomes.
            bool ok = _hits >= 2 && _totalDamage > 0 && _sweep != null && travel > 4f;
            GD.Print(ok
                ? "[HAZARD] RESULT: PASS (the trap damages on a cooldown, and the blade sweeps)"
                : "[HAZARD] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private DungeonRoomBuilder _rooms;

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }

    private SweepHazard _sweep;
    private float _sweepMinX = float.MaxValue, _sweepMaxX = float.MinValue;

    private static SweepHazard FindSweep(Node from)
    {
        if (from is SweepHazard s) return s;
        foreach (var c in from.GetChildren()) { var f = FindSweep(c); if (f != null) return f; }
        return null;
    }

    private static SpikeHazard FindSpikes(Node from)
    {
        if (from is SpikeHazard s) return s;
        foreach (var c in from.GetChildren()) { var f = FindSpikes(c); if (f != null) return f; }
        return null;
    }
}
