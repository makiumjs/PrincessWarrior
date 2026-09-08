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
    /// Units per second, applied with MoveToward -- so this is a LINEAR decay
    /// and the number is a lifetime, not a feel. At the old 6.0 it removed 0.1
    /// per frame at 60fps, which meant the 0.08 landing punch was gone in less
    /// than one frame and had never been visible: the camera lerps toward its
    /// desired position, so a single-frame offset barely moves it at all.
    /// Measured on the parry, which starts at 0.22: two frames after the hit
    /// it read 0.020. At 1.2 the same punch lasts about 0.18s.
    [Export] public float ShakeDecaySpeed = 1.2f;
    /// A perfect parry is the hardest thing in the game to do and, until this,
    /// the quietest: a sound cue and nothing on screen. Nearly three times the
    /// landing punch, because it has to read as a different KIND of event, not
    /// a bigger version of stepping off a ledge.
    [Export] public float ParryShakeStrength = 0.22f;

    /// Horizontal recoil, in metres of camera focus. The parry values were
    /// literals inside OnParried; a blow taken had none at all, which left the
    /// most violent thing that happens to the player as the only impact the
    /// frame did not move for.
    [Export] public float ParryPushPerfect { get; set; } = 0.35f;
    [Export] public float ParryPushBlocked { get; set; } = 0.15f;
    [Export] public float DamagePushStrength { get; set; } = 0.30f;

    /// Harder than a parry, and it should be: a flashover is the room going
    /// off, not a blow being turned aside. 0.35 against the parry's 0.22 is a
    /// difference the player feels without having to be told there is one.
    [Export] public float FlashoverShakeStrength = 0.35f;

    [ExportGroup("Dynamic Framing")]
    [Export] public float BaseOrthoSize { get; set; } = 10.5f;
    [Export] public float ArenaOrthoSize { get; set; } = 12.5f;
    [Export] public float BossOrthoSize { get; set; } = 9.0f;
    [Export] public float FramingTransitionSpeed { get; set; } = 2.2f;

    private Vector2 _focus;          // dead-zone-tracked focus point, world X/Y
    private float _lookAheadCurrent; // smoothed look-ahead offset (signed, along X)
    private float _shakeMagnitude;
    private float _parryPushX;
    private bool _bossActive;
    private bool _initialized;

    public override void _Ready()
    {
        Target = GetNodeOrNull<PlayerController>(TargetPath);

        if (LostCrownlike.Core.EventBus.Instance != null)
        {
            LostCrownlike.Core.EventBus.Instance.Landed += OnLanded;
            LostCrownlike.Core.EventBus.Instance.Parried += OnParried;
            LostCrownlike.Core.EventBus.Instance.BossStateChanged += OnBossStateChanged;
            LostCrownlike.Core.EventBus.Instance.Flashover += OnFlashover;
            LostCrownlike.Core.EventBus.Instance.PlayerDamaged += OnPlayerDamaged;
        }
    }

    public override void _ExitTree()
    {
        if (LostCrownlike.Core.EventBus.Instance != null)
        {
            LostCrownlike.Core.EventBus.Instance.Landed -= OnLanded;
            LostCrownlike.Core.EventBus.Instance.Parried -= OnParried;
            LostCrownlike.Core.EventBus.Instance.BossStateChanged -= OnBossStateChanged;
            LostCrownlike.Core.EventBus.Instance.Flashover -= OnFlashover;
            LostCrownlike.Core.EventBus.Instance.PlayerDamaged -= OnPlayerDamaged;
        }
    }

    private void OnBossStateChanged(bool alive, float healthFraction, bool armoured, bool enraged) =>
        _bossActive = alive;

    /// Read-only, for checks: shake is the only part of the camera's response
    /// that leaves no trace in its position once it has decayed.
    public float ShakeMagnitude => _shakeMagnitude;

    private void OnLanded() => _shakeMagnitude = LandingShakeStrength;

    /// The shake is the camera's, not the HUD's. Both listen to the same
    /// signal and neither knows the other exists -- the HUD flashes the screen,
    /// this moves it, and the one place that would have coupled them (a HUD
    /// reaching for a Camera3D) never has to be written.
    ///
    /// Max, not assignment: a flashover during a landing must not come out
    /// SMALLER than the landing did.
    private void OnFlashover(Vector3 atPosition) =>
        _shakeMagnitude = Mathf.Max(_shakeMagnitude, FlashoverShakeStrength);

    /// <summary>
    /// A blow taken shoves the frame the way the blow shoves the player: AWAY
    /// from whatever hit them. The parry recoil goes the other way -- toward the
    /// blade -- because a parry is the player holding their ground, and the two
    /// reading differently is the point.
    ///
    /// Assigned rather than accumulated, and only when it is bigger: two hits
    /// in the same second must not sum into a camera that slides off the level.
    /// </summary>
    private void OnPlayerDamaged(int amount, Vector3 sourcePosition, Vector3 knockback, bool isCritical)
    {
        if (Target == null || !IsInstanceValid(Target)) return;

        float dir = Mathf.Sign(Target.GlobalPosition.X - sourcePosition.X);
        if (dir == 0f) dir = -Target.FacingSign;

        float push = dir * DamagePushStrength;
        if (Mathf.Abs(push) > Mathf.Abs(_parryPushX)) _parryPushX = push;
    }

    /// An ordinary parry gets a fraction of it. The two have to be told apart
    /// by feel as well as by ear, or the player never learns which one they
    /// just did -- and learning that is the whole skill.
    private void OnParried(bool perfect, Vector3 at)
    {
        _shakeMagnitude = perfect ? ParryShakeStrength : ParryShakeStrength * 0.35f;
        float pushDir = Target != null ? Mathf.Sign(Target.GlobalPosition.X - at.X) : 0f;
        if (pushDir == 0f && Target != null) pushDir = -Target.FacingSign;
        _parryPushX = pushDir * (perfect ? ParryPushPerfect : ParryPushBlocked);
    }

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

        if (Mathf.Abs(_parryPushX) > 0.001f)
            _parryPushX = Mathf.MoveToward(_parryPushX, 0f, ShakeDecaySpeed * dt);
        else
            _parryPushX = 0f;

        var desired = new Vector3(
            _focus.X + _lookAheadCurrent + _parryPushX,
            _focus.Y + HeightOffset - _shakeMagnitude,
            targetPos.Z + CameraDistance);

        var pos = GlobalPosition;
        pos.X = Mathf.Lerp(pos.X, desired.X, 1f - Mathf.Exp(-HorizontalSmoothSpeed * dt));
        pos.Y = Mathf.Lerp(pos.Y, desired.Y, 1f - Mathf.Exp(-VerticalSmoothSpeed * dt));
        pos.Z = desired.Z;
        GlobalPosition = pos;

        // Dynamic Orthographic Framing (Item 07 of Craft Plan):
        // Pull back in an arena to give tactical awareness across all threats,
        // push in on the Warden for an intimate, dramatic duel.
        if (Projection == ProjectionType.Orthogonal)
        {
            bool inArena = false;
            if (Target != null && !_bossActive)
            {
                var gates = GetTree().GetNodesInGroup(LostCrownlike.World.ArenaGate.GroupName);
                foreach (var node in gates)
                {
                    if (node is LostCrownlike.World.ArenaGate gate && !gate.IsOpen
                        && Target.GlobalPosition.X >= gate.SpanMinX && Target.GlobalPosition.X <= gate.SpanMaxX)
                    {
                        inArena = true;
                        break;
                    }
                }
            }

            float targetSize = _bossActive ? BossOrthoSize : (inArena ? ArenaOrthoSize : BaseOrthoSize);
            if (Mathf.Abs(Size - targetSize) > 0.01f)
            {
                Size = Mathf.MoveToward(Size, targetSize, FramingTransitionSpeed * dt);
            }
        }

        // Fixed orientation - no roll/pitch/yaw drift, keeps the 2.5D read.
        // Pitch, not zero: a perfectly level camera — orthographic especially —
        // sees a flat floor exactly edge-on, so the walkable surface collapses
        // into its 0.15m untextured edge and the level reads as a wall with a
        // grey stripe. A few degrees of downward tilt is what 2.5D
        // side-scrollers actually use.
        RotationDegrees = new Vector3(-PitchDegrees, 0f, 0f);
    }
}
