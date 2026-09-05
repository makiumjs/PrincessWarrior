using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// TEST-SCENE ONLY: the grace period that lets a jump still fire just after
/// walking off a ledge.
///
/// Found by mutation testing: zeroing CoyoteTimeWindow broke no check at all.
/// It is pure game feel — removing it makes every ledge jump feel like a
/// betrayal — and it had no coverage whatsoever.
///
/// The test walks the player off an edge, waits until it is airborne and
/// falling, then presses jump INSIDE the window. The jump must fire.
public partial class CoyoteTimeTest : Node
{
    private int _f;
    private PlayerController _player;
    private bool _jumped;
    private bool _lateJumpFired;
    private int _phase;   // 0 = grace must fire, 1 = late press must NOT
    private bool _wasOnFloor;
    private int _leftGroundFrame = -1;
    private float _elapsedAirborne = -1f;
    private float _lateElapsed = -1f;

    public override void _Ready() =>
        EventBus.Instance.Jumped += () =>
        {
            if (_phase == 0) { _jumped = true; GD.Print($"[COYOTE] grace jump fired at frame {_f}"); }
            else { _lateJumpFired = true; GD.Print($"[COYOTE] LATE jump fired at frame {_f} - grace is unbounded"); }
        };

    /// Marches right along the ground the player is standing on until the
    /// floor stops, and returns that point.
    private Vector3? FindGroundEdge()
    {
        var space = _player.GetWorld3D()?.DirectSpaceState;
        if (space == null) return null;
        for (float dx = 1f; dx < 40f; dx += 0.5f)
        {
            var from = _player.GlobalPosition + new Vector3(dx, 0.3f, 0f);
            var q = PhysicsRayQueryParameters3D.Create(from, from + new Vector3(0f, -2f, 0f), PhysicsLayers.World);
            if (space.IntersectRay(q).Count == 0)
                return _player.GlobalPosition + new Vector3(dx, 0f, 0f);
        }
        return null;
    }

    public override void _Process(double delta)
    {
        _f++;
        _player ??= GetTree().GetFirstNodeInGroup("player") as PlayerController;
        if (_player == null) return;

        // Walking there from the spawn does not work: the room's step-up
        // blocks a player who cannot jump yet, so it never reaches an edge and
        // the test silently measures nothing. Place it ON a lip instead.
        if (_f == 20)
        {
            var edge = FindGroundEdge();
            if (edge == null)
            {
                GD.Print("[COYOTE] RESULT: FAIL (no platform edge found)");
                GetTree().Quit();
                return;
            }
            _player.GlobalPosition = edge.Value + new Vector3(-1.2f, 0.6f, 0f);
            _player.Velocity = Vector3.Zero;
            GD.Print($"[COYOTE] placed just before the lip at {_player.GlobalPosition}");
        }
        if (_f > 30) Input.ActionPress("move_right");

        bool onFloor = _player.IsOnFloor();
        if (_f > 25 && _wasOnFloor && !onFloor && _leftGroundFrame < 0)
        {
            _leftGroundFrame = _f;
            _elapsedAirborne = 0f;
            GD.Print($"[COYOTE] left the ground at frame {_f}");
        }
        _wasOnFloor = onFloor;

        // Press at a FIXED instant after leaving the ledge, not at a fraction
        // of CoyoteTimeWindow. Deriving it from the value under test means a
        // larger window simply moves the press later and the test passes no
        // matter what — the same trap that let a broken wall-slide clamp and a
        // zeroed dash i-frame window both go unnoticed here.
        // 0.05s: inside a healthy 0.1s grace, outside a zeroed one.
        const float PressAt = 0.05f;
        if (_elapsedAirborne >= 0f && !_jumped)
        {
            _elapsedAirborne += (float)delta;
            if (_elapsedAirborne >= PressAt) Input.ActionPress("jump");
            if (_elapsedAirborne > 0.6f) _elapsedAirborne = -2f;   // long past any sane window
        }

        // Phase 2: the other half of the mechanic. A grace period that never
        // expires is as wrong as one that does not exist — it would let a
        // player jump seconds after walking into a pit. Step off again and
        // press well outside any sane window; nothing must happen.
        if (_jumped && _phase == 0 && _player.IsOnFloor() && _f > 60)
        {
            _phase = 1;
            _lateElapsed = -1f;
            Input.ActionRelease("jump");
            var edge2 = FindGroundEdge();
            if (edge2 != null)
            {
                _player.GlobalPosition = edge2.Value + new Vector3(0.8f, 0.4f, 0f);
                _player.Velocity = Vector3.Zero;
                _lateElapsed = 0f;
                GD.Print("[COYOTE] phase 2: stepped off again, will press jump 0.8s later");
            }
        }

        if (_lateElapsed >= 0f)
        {
            _lateElapsed += (float)delta;
            if (_lateElapsed >= 0.8f) Input.ActionPress("jump");
        }

        if (_f == 600)
        {
            GD.Print($"[COYOTE] window={_player.CoyoteTimeWindow}s graceJump={_jumped} lateJump={_lateJumpFired} phase={_phase}");
            if (_leftGroundFrame < 0)
                GD.Print("[COYOTE] RESULT: FAIL (player never left a ledge - nothing was tested)");
            else if (!_jumped)
                GD.Print("[COYOTE] RESULT: FAIL (jump inside the grace window did not fire)");
            else if (_phase == 0)
                GD.Print("[COYOTE] RESULT: FAIL (never reached the second phase - the bound was not tested)");
            else if (_lateJumpFired)
                GD.Print("[COYOTE] RESULT: FAIL (a jump 0.8s after leaving the ledge still fired - grace is unbounded)");
            else
                GD.Print("[COYOTE] RESULT: PASS (grace fires just after a ledge, and has expired 0.8s later)");
            GetTree().Quit();
        }
    }
}
