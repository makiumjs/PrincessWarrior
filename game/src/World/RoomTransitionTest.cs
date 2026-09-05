using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: teleports the player onto the room exit and checks the room
/// actually regenerates into a DIFFERENT layout, and that the player ends up at
/// the new room's start rather than stranded where the old geometry used to be.
public partial class RoomTransitionTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private float _exitX;
    private int _childrenBefore;
    private bool _transitioned;

    public override void _Ready()
    {
        EventBus.Instance.LevelTransitionRequested += (path, spawn) =>
        {
            _transitioned = true;
            GD.Print($"[TRANS] transition requested: {path} -> {spawn} (frame {_f})");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= GetTree().CurrentScene?.GetNodeOrNull<DungeonRoomBuilder>("Room");
        if (_player == null || _room == null) return;

        if (_f == 10)
        {
            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit == null) { GD.Print("[TRANS] RESULT: FAIL (no RoomExit in room)"); GetTree().Quit(); return; }
            _exitX = exit.GlobalPosition.X;
            _childrenBefore = _room.GetChildCount();
            GD.Print($"[TRANS] room 0: {_childrenBefore} nodes, exit at x={_exitX:F1}");
            _player.GlobalPosition = exit.GlobalPosition;
        }

        if (_f == 60)
        {
            int after = _room.GetChildCount();
            RoomExitTrigger newExit = null;
            foreach (var c in _room.GetChildren())
                if (c is RoomExitTrigger t && !t.IsQueuedForDeletion()) { newExit = t; break; }
            GD.Print($"[TRANS] exit node name in new room: {(newExit == null ? "NONE" : newExit.Name)}");
            float newExitX = newExit?.GlobalPosition.X ?? -1f;
            var p = _player.GlobalPosition;
            GD.Print($"[TRANS] room {_room.RoomIndex}: {after} nodes, exit at x={newExitX:F1}, player at {p}");

            bool changed = _room.RoomIndex == 1 && !Mathf.IsEqualApprox(newExitX, _exitX);
            bool atStart = p.X < 6f;
            GD.Print(_transitioned && changed && atStart
                ? "[TRANS] RESULT: PASS (new layout, player at its start)"
                : $"[TRANS] RESULT: FAIL (transitioned={_transitioned} changed={changed} atStart={atStart})");
            GetTree().Quit();
        }
    }
}
