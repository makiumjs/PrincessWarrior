using Godot;

namespace LostCrownlike.PlayerCamera;

/// <summary>
/// Side-scroll camera rig for the coupled movement/physics/camera trio (see
/// ARCHITECTURE.md). Follows <see cref="Target"/> on the X/Y plane at a
/// fixed Z offset, with a dead-zone before it starts tracking and a
/// look-ahead bias in the player's facing direction. Deliberately does not
/// rotate or free-fly - it stays camera-plane-locked so it reads as a
/// classic 2.5D platformer camera.
/// </summary>
[GlobalClass]
public partial class SideScrollCamera : Camera3D
{
    // Exported as a NodePath and resolved in _Ready (matches the convention
    // used everywhere else in the codebase, e.g. CombatController.PlayerPath,
    // EnemyController.PatrolPointAPath) rather than a directly-typed Node
    // export - a directly-typed custom-class [Export] field does not reliably
    // bind from the .tscn NodePath() assignment in this Godot Mono setup.
    /// Downward tilt of the rig, degrees. 0 = perfectly level.
    [Export] public float PitchDegrees { get; set; } = 8f;

    [Export] public NodePath TargetPath;

    public PlayerController Target { get; private set; }

    [ExportGroup("Framing")]
    [Export] public float CameraDistance = 12f;    // Z offset from the movement plane
    [Export] public float HeightOffset = 1.4f;      // vertical offset above the target's origin

    [ExportGroup("Dead zone")]
    [Export] public float DeadZoneWidth = 2.0f;     // total width (both sides) the target can move within before the camera tracks
    [Export] public float DeadZoneHeight = 1.5f;    // total height, same idea on Y

    [ExportGroup("Look-ahead")]
    [Export] public float LookAheadDistance = 2.5f;
    [Export] public float LookAheadSmoothSpeed = 4f;

    [ExportGroup("Smoothing")]
    [Export] public float HorizontalSmoothSpeed = 6f;
    [Export] public float VerticalSmoothSpeed = 5f;

    [ExportGroup("Feel")]
    [Export] public float LandingShakeStrength = 0.08f;   // small camera framing punch on landing
    [Export] public float ShakeDecaySpeed = 6f;

    private Vector2 _focus;          // dead-zone-tracked focus point, world X/Y
    private float _lookAheadCurrent; // smoothed look-ahead offset (signed, along X)
    private float _shakeMagnitude;
    private bool _initialized;

    public override void _Ready()
    {
        Target = GetNodeOrNull<PlayerController>(TargetPath);

        if (LostCrownlike.Core.EventBus.Instance != null)
            LostCrownlike.Core.EventBus.Instance.Landed += OnLanded;
    }

    public override void _ExitTree()
    {
        if (LostCrownlike.Core.EventBus.Instance != null)
            LostCrownlike.Core.EventBus.Instance.Landed -= OnLanded;
    }

    private void OnLanded() => _shakeMagnitude = LandingShakeStrength;

    public override void _Process(double delta)
    {
        if (Target == null || !IsInstanceValid(Target))
            return;

        var dt = (float)delta;
        var targetPos = Target.GlobalPosition;

        if (!_initialized)
        {
            _focus = new Vector2(targetPos.X, targetPos.Y);
            GlobalPosition = new Vector3(_focus.X, _focus.Y + HeightOffset, targetPos.Z + CameraDistance);
            _initialized = true;
        }

        // Dead-zone: only move the focus point once the target has left the
        // rectangle centered on it, and only by the overshoot amount - this
        // is what makes the camera hold still during small player wiggle.
        var dx = targetPos.X - _focus.X;
        var halfW = DeadZoneWidth * 0.5f;
        if (dx > halfW) _focus.X += dx - halfW;
        else if (dx < -halfW) _focus.X += dx + halfW;

        var dy = targetPos.Y - _focus.Y;
        var halfH = DeadZoneHeight * 0.5f;
        if (dy > halfH) _focus.Y += dy - halfH;
        else if (dy < -halfH) _focus.Y += dy + halfH;

        // Look-ahead biases the framing toward the direction the player is
        // facing, smoothed so it doesn't snap on every direction change.
        var lookAheadTarget = Target.FacingSign * LookAheadDistance;
        _lookAheadCurrent = Mathf.Lerp(_lookAheadCurrent, lookAheadTarget, 1f - Mathf.Exp(-LookAheadSmoothSpeed * dt));

        if (_shakeMagnitude > 0f)
            _shakeMagnitude = Mathf.MoveToward(_shakeMagnitude, 0f, ShakeDecaySpeed * dt);

        var desired = new Vector3(
            _focus.X + _lookAheadCurrent,
            _focus.Y + HeightOffset - _shakeMagnitude,
            targetPos.Z + CameraDistance);

        var pos = GlobalPosition;
        pos.X = Mathf.Lerp(pos.X, desired.X, 1f - Mathf.Exp(-HorizontalSmoothSpeed * dt));
        pos.Y = Mathf.Lerp(pos.Y, desired.Y, 1f - Mathf.Exp(-VerticalSmoothSpeed * dt));
        pos.Z = desired.Z;
        GlobalPosition = pos;

        // Fixed orientation - no roll/pitch/yaw drift, keeps the 2.5D read.
        // Pitch, not zero: a perfectly level camera — orthographic especially —
        // sees a flat floor exactly edge-on, so the walkable surface collapses
        // into its 0.15m untextured edge and the level reads as a wall with a
        // grey stripe. A few degrees of downward tilt is what 2.5D
        // side-scrollers actually use.
        RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f);
    }
}
