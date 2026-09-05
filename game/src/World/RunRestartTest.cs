using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: finishing a run leads somewhere.
///
/// The arc shipped with an ending that was a dead end: the exit of the last
/// room emitted RunCompleted, the HUD showed a banner, and nothing else
/// happened -- no restart, no menu. Reported from play as "there is no way to
/// finish the room", which was accurate: the player reached the end and the
/// game simply stopped responding to progress.
///
/// Asserts the whole loop, because every part of it failed silently before:
/// the run ends, the restart puts the player back in room 0, progression is
/// wiped, and the save on disk no longer says the run is finished.
public partial class RunRestartTest : Node
{
    private int _f;
    private Node3D _player;
    private DungeonRoomBuilder _room;
    private int _completions;
    private bool _restartPressed;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as Node3D;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 15)
        {
            EventBus.Instance.RunCompleted += _ => _completions++;
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.Dash);
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.DoubleJump);
        }

        // Walk the run to its end by crossing each exit.
        if (_f > 30 && _f % 45 == 0 && _completions == 0)
        {
            var exit = _room.GetNodeOrNull<Node3D>("RoomExit");
            if (exit != null) _player.GlobalPosition = exit.GlobalPosition;
        }

        // Press repeatedly rather than once. IsActionJustPressed is true for a
        // single frame, and whether the room builder's _Process runs before or
        // after this node's in that frame is scene-order, not something a test
        // should depend on.
        if (_completions > 0 && _room.RoomIndex != 0)
        {
            if (_f % 10 == 0) Input.ActionPress("jump");
            else if (_f % 10 == 3) Input.ActionRelease("jump");
            _restartPressed = true;
        }
        else if (_restartPressed)
        {
            Input.ActionRelease("jump");
        }

        if (_f == 700)
        {
            var save = Save.SaveManager.Instance?.Current;
            var abilities = (AbilityFlags)(int)_player.Get("UnlockedAbilities");

            GD.Print($"[RESTART] completions={_completions} roomIndex={_room.RoomIndex} " +
                     $"abilities={abilities} saved: rooms={save?.RoomsCleared} done={save?.RunCompleted}");

            bool ok = _completions == 1
                   && _room.RoomIndex == 0                  // back at the start
                   && abilities == AbilityFlags.None        // progression wiped
                   && save != null && !save.RunCompleted    // and the disk agrees
                   && save.RoomsCleared == 0;

            GD.Print(ok
                ? "[RESTART] RESULT: PASS (the run ends, and jump starts a fresh one)"
                : "[RESTART] RESULT: FAIL");
            GetTree().Quit();
        }
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
