using Godot;
using LostCrownlike.Core;
using LostCrownlike.PlayerCamera;

namespace LostCrownlike.World;

/// TEST-SCENE ONLY: can a wall jump actually climb a shaft of the size the
/// generator builds?
///
/// The traversal bot stalls at 14% of the WallShaft layout, and a bot that
/// cannot climb proves nothing about whether the shaft is climbable. So this
/// takes the level generator out of it: two walls are built here, at exactly
/// the dimensions MicroChunk computes -- gap = max(SafeGap * 0.55, 2), height =
/// MaxDoubleJumpUp * 1.6 * intensity -- and the player is driven by the
/// controller's OWN preconditions rather than by a heuristic: hold into the
/// wall, wait until it is falling, press jump once per contact.
///
/// If height accumulates, the shaft is fine and the bot is the limitation. If
/// it does not, a room in the shipped game cannot be finished.
public partial class WallShaftClimbTest : Node
{
    private int _f;
    private CharacterBody3D _player;
    private PlayerMetrics _m;

    /// 1.0 is the saturated difficulty of rooms 3 and up.
    [Export] public float Intensity = 1.0f;

    private float _gap;
    private float _height;
    private float _baseY;
    private float _maxY;
    private int _wallJumpEvents;
    private bool _jumpHeld;
    private int _lastKickFrame = -99;

    public override void _Ready() => _m = new PlayerMetrics();

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as CharacterBody3D;
        if (_player == null) return;

        if (_f == 5)
        {
            EventBus.Instance.WallJumped += () => _wallJumpEvents++;

            // FULL difficulty, not room 1's 0.7. The ramp saturates at room
            // index 3, so rooms 3-5 build the tallest shaft the generator can
            // produce -- and those are exactly the rooms the traversal bot
            // cannot finish. Testing the easy shaft would have measured the
            // case that already works.
            _gap = Mathf.Max(_m.SafeGap * 0.55f, 2.0f);
            _height = _m.MaxDoubleJumpUp * 1.6f * Intensity;

            var at = _player.GlobalPosition + new Vector3(0f, 0f, 0f);
            BuildWall(at.X - _gap * 0.5f - 0.5f, at.Y - 1f, _height);
            BuildWall(at.X + _gap * 0.5f + 0.5f, at.Y - 1f, _height);

            _player.Set("UnlockedAbilities", (int)(AbilityFlags.DoubleJump | AbilityFlags.Dash | AbilityFlags.WallJump));
            _baseY = at.Y;
            _maxY = at.Y;
            GD.Print($"[SHAFT] gap {_gap:F2}, height {_height:F2}, player starts at y {_baseY:F2}");
        }

        if (_f < 20) return;

        Climb();
        _maxY = Mathf.Max(_maxY, _player.GlobalPosition.Y);

        if (_f == 600)
        {
            float climbed = _maxY - _baseY;
            GD.Print($"[SHAFT] wall jumps the game performed: {_wallJumpEvents}");
            GD.Print($"[SHAFT] height climbed: {climbed:F2} of the {_height:F2} shaft " +
                     $"({(_wallJumpEvents > 0 ? climbed / _wallJumpEvents : 0f):F2} per jump)");

            bool ok = _wallJumpEvents >= 3 && climbed >= _height;
            GD.Print(ok
                ? "[SHAFT] RESULT: PASS (wall jumps climb a shaft of the size the generator builds)"
                : "[SHAFT] RESULT: FAIL");
            GetTree().Quit();
        }
    }

    /// Drives the controller's stated preconditions instead of guessing at
    /// them: PlayerController allows a wall jump only while the player presses
    /// INTO the wall and is already falling.
    private void Climb()
    {
        bool onFloor = _player.IsOnFloor();
        bool onWall = _player.IsOnWall();

        // Every press must be a clean EDGE. PlayerController fills its jump
        // buffer from IsActionJustPressed, and pressing an action that is
        // already held produces no edge at all -- the ground jump was still
        // held when the wall press came, so the buffer never filled and the
        // player wall-SLID for 600 frames (vy pinned at -3.0, the slide clamp)
        // without ever wall-JUMPING. So: decide whether a press is wanted, and
        // route every press through one release-then-press gate.
        bool wantJump;

        if (onFloor)
        {
            Hold(1);
            wantJump = _f % 20 == 0;
        }
        else if (onWall)
        {
            float nx = _player.GetWallNormal().X;
            int into = nx > 0f ? -1 : 1;
            Hold(into);
            wantJump = _player.Velocity.Y <= 0f && _f > _lastKickFrame + 8;
        }
        else
        {
            wantJump = false;
        }

        if (wantJump && !_jumpHeld)
        {
            Input.ActionPress("jump");
            _jumpHeld = true;
            _lastKickFrame = _f;
        }
        else if (_jumpHeld && _f >= _lastKickFrame + 2)
        {
            Input.ActionRelease("jump");
            _jumpHeld = false;
        }
    }

    private static void Hold(int dir)
    {
        if (dir >= 0) { Input.ActionRelease("move_left"); Input.ActionPress("move_right"); }
        else          { Input.ActionRelease("move_right"); Input.ActionPress("move_left"); }
    }

    private void BuildWall(float x, float y, float height)
    {
        var body = new StaticBody3D
        {
            Position = new Vector3(x, y + height * 0.5f, 0f),
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(1f, height, 2f) },
        });
        GetTree().CurrentScene.AddChild(body);
    }
}
