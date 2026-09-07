using Godot;
using LostCrownlike.Core;
using LostCrownlike.World;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: the one state random mashing cannot reach.
///
/// WallSlide needs the player airborne, touching a wall, holding INTO it, and
/// NOT pressing jump — a combination random input breaks within a frame or two.
/// So it gets a deliberate test rather than being left as the single mechanic
/// nobody would notice breaking.
public partial class WallSlideTest : Node
{
    private int _f;
    private PlayerController _player;
    private DungeonRoomBuilder _room;
    private Node3D _wall;
    private bool _sawSlide;
    private int _slidingFor;
    private float _fastestFall = 0f;

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        _room ??= FindRoom(GetTree().Root);
        if (_player == null || _room == null) return;

        if (_f == 10)
        {
            EventBus.Instance.EmitAbilityUnlocked(AbilityFlags.WallJump);
            // Asked, not remembered: the ability gate can move which room
            // carries the shaft, and it did.
            _room.RebuildAs(_room.FirstRoomWith(World.ChunkKind.WallShaft));
        }

        if (_f == 25)
        {
            _wall = FindClimbableWall(_room);
            if (_wall == null)
            {
                GD.Print("[SLIDE] RESULT: FAIL (no climbable wall in the room)");
                GetTree().Quit();
                return;
            }
            // Against the wall's right face, two thirds of the way up, so there
            // is surface left below to slide along whatever the wall's height.
            float up = WallHeight(_wall) * 0.33f;
            _player.GlobalPosition = _wall.GlobalPosition + new Vector3(0.7f, up, 0f);
            _player.Velocity = Vector3.Zero;
            GD.Print($"[SLIDE] placed against wall at {_player.GlobalPosition}");
        }

        // Hold INTO the wall (left) and never press jump.
        if (_f is > 25 and < 200)
        {
            Input.ActionPress("move_left");
            Input.ActionRelease("jump");

            bool sliding = _player.CurrentState == MovementState.WallSlide;
            if (sliding)
            {
                _sawSlide = true;
                _slidingFor++;

                // The SUSTAINED speed, not the instant of entry. Entering a
                // wall slide does not erase the velocity the player arrived
                // with, and _Process reads a value _PhysicsProcess has not
                // clamped yet, so the first frames in the state legitimately
                // show free fall. Traced: -20 on entry, then -3.00 flat from
                // the fifth frame on, in a state whose cap is 3.
                //
                // It only surfaced when the ability gate moved the wall shaft
                // to a later, taller room, where the player arrives faster --
                // the measurement had been wrong all along and the drop was
                // too short to show it.
                if (_slidingFor > 4)
                {
                    _fastestFall = Mathf.Min(_fastestFall, _player.Velocity.Y);
                }
            }
            else _slidingFor = 0;
        }

        if (_f == 200)
        {
            Input.ActionRelease("move_left");
            GD.Print($"[SLIDE] wall slide seen={_sawSlide} fastest fall while sliding={_fastestFall:F2} " +
                     $"(WallSlideSpeed={_player.WallSlideSpeed}; must stay slower than -8 m/s free fall)");
            // Fixed bound, NOT the tunable being tested. The previous version
            // asserted against WallSlideSpeed itself, so raising that value to
            // 999 — removing the clamp entirely — made the assertion trivially
            // true and the test kept passing on a broken mechanic.
            const float FreeFallIsFasterThan = 8f;
            GD.Print(_sawSlide && _fastestFall > -FreeFallIsFasterThan
                ? "[SLIDE] RESULT: PASS (state entered and the fall is clamped)"
                : "[SLIDE] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// The TALLEST wall, not the first one found. Shaft height scales with the
    /// room's difficulty, so the first wall in an early room can be barely over
    /// the 3-metre bar this looks for -- and the check then placed the player
    /// 2.5m up its face, which is most of the way to the top of it. It measured
    /// a free fall past a short wall and reported "no wall slide", which is
    /// true and is not the defect it was written to catch.
    private static Node3D FindClimbableWall(Node from)
    {
        Node3D best = null;
        float tallest = 0f;
        void Walk(Node n)
        {
            if (n is StaticBody3D b && b.GetChildCount() > 0
                && b.GetChild(0) is CollisionShape3D cs
                && cs.Shape is BoxShape3D box && box.Size.Y > 3f && box.Size.X < 1.5f
                && box.Size.Y > tallest)
            {
                tallest = box.Size.Y;
                best = b;
            }
            foreach (var c in n.GetChildren()) Walk(c);
        }
        Walk(from);
        return best;
    }

    /// How tall the wall this check picked actually is, so the player can be
    /// placed as a FRACTION of it rather than at a fixed 2.5 metres.
    private static float WallHeight(Node3D wall) =>
        wall.GetChild(0) is CollisionShape3D cs && cs.Shape is BoxShape3D box ? box.Size.Y : 0f;

    private static DungeonRoomBuilder FindRoom(Node from)
    {
        if (from is DungeonRoomBuilder b) return b;
        foreach (var c in from.GetChildren()) { var f = FindRoom(c); if (f != null) return f; }
        return null;
    }
}
