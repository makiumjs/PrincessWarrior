using Godot;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: the moving floor actually carries, in both directions.
///
/// A conveyor that does not convey is a stretch of ordinary corridor with a
/// story attached, and nothing else in the game would notice: the traversal bot
/// crossed this chunk in 289 frames whether or not the floor moved, because it
/// holds right and the drag is a fraction of its run speed. Crossability is the
/// wrong question here.
///
/// So the player is stood still on each stretch WITH NO INPUT and its drift is
/// measured. Standing still is the only state in which a floor's own motion is
/// the only thing moving you.
public partial class CurrentTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private float _againstStart, _againstDrift, _withStart, _withDrift;
    private Vector3 _againstAt, _withAt;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindFirst<DungeonRoomBuilder>(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 10) { _room.SpawnEnemies = false; _room.ChunkUnderTest = "Current"; _room.RebuildAs(0); return; }
        if (_room.IsRebuilding) return;

        if (_f == 30)
        {
            // Found from the composed spans rather than from a remembered
            // coordinate: the chunk's own lengths decide where the stretches
            // are, and they have already moved once.
            var spans = _room.LastConveyors;
            if (spans.Count < 2) { Done(false, $"expected two conveyor spans, found {spans.Count}"); return; }
            _againstAt = new Vector3((spans[0].X0 + spans[0].X1) * 0.5f, 1.4f, 0f);
            _withAt = new Vector3((spans[1].X0 + spans[1].X1) * 0.5f, 1.4f, 0f);
            GD.Print($"[CURRENT] stretches at x={_againstAt.X:F1} ({spans[0].Velocity:F1} m/s) " +
                     $"and x={_withAt.X:F1} ({spans[1].Velocity:F1} m/s)");
        }

        if (_f == 40) { _player.GlobalPosition = _againstAt; _againstStart = _againstAt.X; return; }
        if (_f == 100) _againstDrift = _player.GlobalPosition.X - _againstStart;

        if (_f == 110) { _player.GlobalPosition = _withAt; _withStart = _withAt.X; return; }
        if (_f == 170) _withDrift = _player.GlobalPosition.X - _withStart;

        if (_f == 180)
        {
            GD.Print($"[CURRENT] standing still for one second: drifted {_againstDrift:F2}m against, " +
                     $"{_withDrift:F2}m with");
            // A metre either way in a second. Absolute, and well under the
            // 3 and 4 m/s the spans carry, so a floor that has merely been
            // nudged does not read as one that carries.
            bool ok = _againstDrift < -1f && _withDrift > 1f;
            Done(ok, ok ? "the floor carries a standing player backwards on one stretch and forwards on the next" : "");
        }
    }

    private void Done(bool ok, string why)
    {
        GD.Print(ok ? $"[CURRENT] RESULT: PASS ({why})" : $"[CURRENT] RESULT: FAIL ({why})");
        GetTree().Quit();
    }

    private static T FindFirst<T>(Node from) where T : Node
    {
        if (from is T t) return t;
        foreach (var c in from.GetChildren()) { var f = FindFirst<T>(c); if (f != null) return f; }
        return null;
    }
}
