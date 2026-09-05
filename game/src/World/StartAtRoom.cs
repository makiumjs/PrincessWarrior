using Godot;

namespace LostCrownlike.World;

/// DEV ENTRY POINT: starts the game at a given room instead of room 0.
///
///   godot --path game scenes/dev/StartAtRoom.tscn -- --room=5
///
/// Rooms 3 and up run at saturated difficulty, and the traversal bot does not
/// finish them; this exists so a person can go and see whether that is the
/// level or the bot. Defaults to 5, the last room of a run.
public partial class StartAtRoom : Node
{
    [Export] public int Room = 5;

    private int _f;
    private bool _done;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!arg.StartsWith("--room=")) continue;
            if (int.TryParse(arg.Substring("--room=".Length), out int r)) Room = r;
        }
    }

    public override void _Process(double delta)
    {
        if (_done) return;

        // Deferred by a few frames: the room builds itself in _Ready, and
        // rebuilding it in the same frame frees nodes the scene tree is still
        // walking.
        _f++;
        if (_f < 10) return;

        var room = Find(GetTree().Root);
        if (room == null) return;

        _done = true;
        room.RebuildAs(Room);
        GD.Print($"[DEV] started at room {Room} of {room.RunLength} " +
                 $"(difficulty {Mathf.Min(0.55f + Room * 0.15f, 1f):F2})");
    }

    private static DungeonRoomBuilder Find(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren())
        {
            var f = Find(c);
            if (f != null) return f;
        }
        return null;
    }
}
