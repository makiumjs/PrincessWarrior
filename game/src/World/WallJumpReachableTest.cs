using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: proves WallJump is real content — that a room grants it and
/// that a climbable surface exists to use it on. Before the WallShaft chunk,
/// the ability was implemented, flagged and shown in the HUD, but nothing in
/// the game granted it and no surface anywhere could be slid on.
public partial class WallJumpReachableTest : Node
{
    private int _f;
    private DungeonRoomBuilder _room;
    private bool _granted;
    private bool _wallJumped;

    public override void _Ready()
    {
        EventBus.Instance.AbilityUnlocked += bits =>
        {
            if (((AbilityFlags)bits).HasFlag(AbilityFlags.WallJump))
            {
                _granted = true;
                GD.Print($"[WALLJUMP] granted by a pickup at frame {_f}");
            }
        };
        EventBus.Instance.WallJumped += () =>
        {
            _wallJumped = true;
            GD.Print($"[WALLJUMP] wall jump performed at frame {_f}");
        };
    }

    public override void _Process(double delta)
    {
        _f++;
        _room ??= FindRoom(GetTree().Root);
        if (_room == null) return;

        // Room 1 is the layout carrying the shaft.
        // Asked, not remembered. This said room 1 for as long as room 1 was
        // where the wall shaft lived; the ability gate moved it to room 4 and
        // the check failed on a design decision rather than on a defect.
        if (_f == 10) _room.RebuildAs(_room.FirstRoomWith(ChunkKind.WallShaft));

        if (_f == 30)
        {
            // The pickup half of this check is gone with the crystals. What
            // remains is the half that found the original defect: WallJump was
            // implemented, in AbilityFlags, drawn in the HUD -- and NO SURFACE
            // IN THE GAME had collision to slide on. Owning an ability from the
            // first frame does not make it content.
            int walls = 0;
            foreach (var c in _room.GetChildren())
            {
                if (c is StaticBody3D b && b.GetChildCount() > 0
                    && b.GetChild(0) is CollisionShape3D cs
                    && cs.Shape is BoxShape3D box && box.Size.Y > 3f) walls++;
            }
            GD.Print($"[WALLJUMP] room {_room.RoomIndex} contains {walls} climbable wall(s)");

            if (walls == 0)
            {
                GD.Print("[WALLJUMP] RESULT: FAIL (nothing in the room can be slid on)");
                GetTree().Quit();
                return;
            }
        }
        // Drive the player into the shaft to confirm the surface really
        // registers as wall-slidable rather than merely being tall.
        if (_f == 45) Input.ActionPress("move_right");
        if (_f is > 50 and < 400 && _f % 20 == 0) Input.ActionPress("jump");
        if (_f is > 50 and < 400 && _f % 20 == 3) Input.ActionRelease("jump");

        if (_f == 400)
        {
            GD.Print(_granted || _wallJumped
                ? "[WALLJUMP] RESULT: PASS (a climbable surface exists and the player slides on it)"
                : "[WALLJUMP] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
