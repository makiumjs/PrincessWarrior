using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: every checkpoint in a room stands on ground.
///
/// A play session in room 5 collected all three abilities and fought all three
/// enemy types, yet banked only shrine_00 -- the one at the spawn. If the later
/// checkpoints cannot be touched, every death sends the player back to the
/// start of the hardest room in the game, which feels exactly like a room that
/// cannot be finished.
///
/// Checked by ray, not by playing: a checkpoint the bot fails to reach might
/// just be a bot that took another route, but a checkpoint with nothing under
/// it is unreachable for anybody.
public partial class CheckpointReachTest : Node
{
    [Export] public int RoomToCheck = 5;

    private int _f;
    private DungeonRoomBuilder _room;

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindRoom(GetTree().Root);
        if (_room == null) return;

        if (_f == 20) { _room.RebuildAs(RoomToCheck); return; }
        if (_f != 60) return;

        var space = GetViewport().World3D.DirectSpaceState;
        int total = 0, grounded = 0;

        foreach (var child in _room.GetChildren())
        {
            if (child is not CheckpointTrigger cp) continue;
            total++;

            var from = cp.GlobalPosition;
            // Straight down, a generous distance: a checkpoint floating three
            // units over a pit is as unreachable as one with nothing under it.
            var to = from + new Vector3(0f, -3f, 0f);
            var q = PhysicsRayQueryParameters3D.Create(from, to, PhysicsLayers.World);
            bool hasFloor = space.IntersectRay(q).Count > 0;
            if (hasFloor) grounded++;

            GD.Print($"[CP] {cp.CheckpointId} at ({from.X:F1}, {from.Y:F1}) floor below: {hasFloor}");
        }

        GD.Print($"[CP] room {RoomToCheck}: {grounded} of {total} checkpoints stand on ground");
        GD.Print(total > 0 && grounded == total
            ? "[CP] RESULT: PASS (every checkpoint in the room has floor under it)"
            : "[CP] RESULT: FAIL");
        GetTree().Quit();
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren())
        {
            var f = FindRoom(c);
            if (f != null) return f;
        }
        return null;
    }
}
