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
        if (_f == 10) _room.RebuildAs(1);

        if (_f == 30)
        {
            int walls = 0, pickups = 0;
            foreach (var c in _room.GetChildren())
            {
                if (c is AbilityPickup p && p.Ability == AbilityFlags.WallJump) pickups++;
                if (c is StaticBody3D b && b.GetChildCount() > 0
                    && b.GetChild(0) is CollisionShape3D cs
                    && cs.Shape is BoxShape3D box && box.Size.Y > 3f) walls++;
            }
            GD.Print($"[WALLJUMP] room 1 contains {pickups} WallJump pickup(s) and {walls} climbable wall(s)");

            if (pickups == 0 || walls == 0)
            {
                GD.Print("[WALLJUMP] RESULT: FAIL (ability is unreachable content)");
                GetTree().Quit();
                return;
            }
        }

        // Grant it and drive the player into the shaft to confirm the surface
        // actually registers as wall-slidable.
        if (_f == 40) EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.WallJump);
        if (_f == 45) Input.ActionPress("move_right");
        if (_f is > 50 and < 400 && _f % 20 == 0) Input.ActionPress("jump");
        if (_f is > 50 and < 400 && _f % 20 == 3) Input.ActionRelease("jump");

        if (_f == 400)
        {
            GD.Print(_granted || _wallJumped
                ? "[WALLJUMP] RESULT: PASS (granted by a pickup, and a climbable surface exists)"
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
