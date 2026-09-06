using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.PlayerCamera;

/// <summary>
/// Player movement + physics/collision response. Owns the full
/// <see cref="MovementState"/> state machine (Idle, Run, Jump, DoubleJump,
/// Fall, Dash, WallSlide, WallJump, Hurt), coyote time, input buffering and
/// dash i-frames. See ARCHITECTURE.md "The coupled trio: PlayerCamera" for
/// the contract this class implements.
///
/// Movement plane convention: X is horizontal (screen-space left/right),
/// Y is vertical (up), Z is the fixed camera/depth axis. The body's Z
/// position and Z rotation are frozen every physics frame; the body may
/// still rotate around Y to visually flip and face its movement direction.
///
/// Implements <see cref="IDamageable"/> so Combat/AI can call
/// <c>TakeDamage</c> directly per the "everyone may call IDamageable"
/// exception in ARCHITECTURE.md's event vocabulary section. Added to the
/// "player" group in _Ready so AI's group-based player lookup (see
/// src/AI/EnemyController.cs) finds this node with no compile-time
/// dependency between subsystems.
/// </summary>
[GlobalClass]
public partial class PlayerController : CharacterBody3D, IDamageable
{
    // ------------------------------------------------------------------
    // Tunable parameters (ARCHITECTURE.md required set)
    // ------------------------------------------------------------------

    [ExportGroup("Jump")]
    [Export] public float JumpHeight = 2.5f;          // meters, apex above takeoff
    [Export] public float JumpApexTime = 0.35f;        // seconds, takeoff -> apex
    [Export] public float DoubleJumpHeightMultiplier = 0.8f; // extra: 2nd jump is a bit weaker
    [Export] public float FallGravityMultiplier = 1.6f;      // extra: snappier fall than rise
    [Export] public float JumpCutMultiplier = 0.5f;           // extra: variable jump height
    [Export] public float MaxFallSpeed = 20f;                 // extra: terminal velocity

    [ExportGroup("Dash")]
    [Export] public float DashDistance = 4.5f;         // meters covered over the dash
    [Export] public float DashDuration = 0.18f;        // seconds
    [Export] public float DashCooldown = 0.5f;         // seconds from dash start to next dash
    [Export] public float DashIFrameDuration = 0.15f;  // seconds of invulnerability from dash start

    [ExportGroup("Timing windows")]
    [Export] public float CoyoteTimeWindow = 0.1f;     // seconds after leaving ground jump still works
    [Export] public float InputBufferWindow = 0.15f;   // seconds a jump/dash press is remembered

    [ExportGroup("Wall")]
    [Export] public float WallJumpImpulse = 13f;             // launch speed magnitude
    [Export] public float WallJumpAngleDegrees = 60f;        // measured from horizontal, away from wall
    [Export] public float WallSlideSpeed = 3f;                // extra: clamped slide-down speed
    [Export] public float WallJumpLockoutDuration = 0.25f;    // extra: can't re-stick same wall immediately

    [ExportGroup("Ground movement")]
    /// Below this Y the player is considered to have fallen out of the level.
    /// Without it a missed jump falls forever: a play-through of the generated
    /// room reached Y = -95 and was still descending, with no death and so no
    /// respawn — the loop simply never closed.
    [Export] public float FallDeathY = -12f;

    [Export] public float MoveSpeed = 8f;              // extra: required to have a run speed at all
    [Export] public float GroundAcceleration = 60f;    // extra
    [Export] public float GroundFriction = 70f;         // extra
    [Export] public float AirControlMultiplier = 0.65f; // extra

    [ExportGroup("Hurt")]
    [Export] public int MaxHealth = 100;                        // extra
    [Export] public float HurtStunDuration = 0.35f;             // extra
    [Export] public float HurtInvulnerabilityDuration = 1.0f;   // extra
    [Export] public float HurtKnockbackHorizontal = 6f;         // extra
    [Export] public float HurtKnockbackVertical = 4f;           // extra

    [ExportGroup("Abilities")]
    // Default unlocks everything except ChargeAttack so this controller and
    // its test scene are fully exercisable standalone, before the World
    // subsystem exists to drive AbilityUnlocked events. World/Save can
    // still override this at runtime (see AbilityUnlocked subscription
    // below) or overwrite it directly from loaded SaveData.
    [Export] public AbilityFlags UnlockedAbilities { get; set; } =
        AbilityFlags.DoubleJump | AbilityFlags.Dash | AbilityFlags.WallJump;

    // ------------------------------------------------------------------
    // Public read state
    // ------------------------------------------------------------------

    public MovementState CurrentState { get; private set; } = MovementState.Idle;

    /// <summary>+1 when facing +X, -1 when facing -X. Additive to the
    /// documented public interface; useful for the camera's look-ahead and
    /// for Combat's future hitbox facing. Not required by ARCHITECTURE.md
    /// but purely additive, so it doesn't break the documented contract.</summary>
    public int FacingSign { get; private set; } = 1;

    public int CurrentHealth { get; private set; }

    // Velocity is inherited from CharacterBody3D (Vector3 Velocity) and is
    // already exactly the documented public property - not redeclared here.

    // ------------------------------------------------------------------
    // Internal state
    // ------------------------------------------------------------------

    private float _gravity;
    private float _jumpVelocity;
    private float _doubleJumpVelocity;

    private float _lockedZ;
    private bool _wasOnFloor;

    /// How fast the player was falling on the frame it touched down, kept
    /// because the landing is detected after the velocity has been zeroed.
    private float _fallSpeedOnLanding;

    /// Below this, a landing raises nothing. Roughly the speed reached falling
    /// off a knee-high ledge.
    [Export] public float DustLandingSpeed = 7f;
    private bool _doubleJumpUsed;

    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private float _dashBufferTimer;

    private bool _isDashing;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private float _dashIFrameTimer;
    private Vector3 _dashDirection = Vector3.Right;

    private bool _isWallJumpLockout;
    private Combat.CombatController _combat;
    private float _wallJumpLockoutTimer;
    private int _wallJumpLockoutSide; // sign of the wall normal we just jumped away from

    private bool _isHurt;
    private float _hurtStunTimer;
    private float _hurtInvulnTimer;
    private Vector3 _spawnPosition;

    private bool _isDead;

    private bool IsInvulnerable => _dashIFrameTimer > 0f || _hurtInvulnTimer > 0f;

    public override void _Ready()
    {
        _lockedZ = GlobalPosition.Z;
        _spawnPosition = GlobalPosition;
        CurrentHealth = MaxHealth;

        // Engine-level 2.5D plane lock. The manual Z/rotation correction later
        // in _PhysicsProcess stays as a backstop, but locking the axes here
        // stops the solver from ever generating off-plane motion in the first
        // place, instead of fixing it up after the fact.
        AxisLockLinearZ = true;
        AxisLockAngularX = true;
        AxisLockAngularY = true;

        CollisionLayer = PhysicsLayers.Player;
        CollisionMask = PhysicsLayers.World;

        AddToGroup("player");

        RecalculateJumpPhysics();

        if (EventBus.Instance != null)
            EventBus.Instance.AbilityUnlocked += OnAbilityUnlocked;
    }

    public override void _ExitTree()
    {
        if (EventBus.Instance != null)
            EventBus.Instance.AbilityUnlocked -= OnAbilityUnlocked;
    }

    private void OnAbilityUnlocked(int abilityBits)
    {
        var ability = (AbilityFlags)abilityBits;
        UnlockedAbilities |= ability;
    }

    /// <summary>Derives gravity and jump takeoff velocities from JumpHeight
    /// / JumpApexTime so the two exported "feel" parameters stay the single
    /// source of truth (h = 0.5*g*t^2, v0 = g*t at a given apex time).</summary>
    private void RecalculateJumpPhysics()
    {
        var t = Mathf.Max(0.01f, JumpApexTime);
        _gravity = 2f * JumpHeight / (t * t);
        _jumpVelocity = 2f * JumpHeight / t;
        _doubleJumpVelocity = _jumpVelocity * DoubleJumpHeightMultiplier;
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        if (_isDead)
        {
            var deadVel = Velocity;
            deadVel.Y -= _gravity * dt;
            deadVel.Z = 0f;
            Velocity = deadVel;
            MoveAndSlide();
            LockPlaneAxes();
            return;
        }

        TickTimers(dt);

        var moveAxis = Input.GetAxis("move_left", "move_right");
        if (Input.IsActionJustPressed("jump"))
            _jumpBufferTimer = InputBufferWindow;
        if (Input.IsActionJustPressed("dash"))
            _dashBufferTimer = InputBufferWindow;

        var grounded = IsOnFloor();

        // Coyote time: full window whenever grounded, ticks down in air.
        if (grounded)
            _coyoteTimer = CoyoteTimeWindow;

        var velocity = Velocity;
        velocity.Z = 0f;

        if (_isHurt)
        {
            velocity.Y -= _gravity * dt;
            velocity = ApplyGroundOrAirFriction(velocity, 0f, grounded, dt);
        }
        else if (_isDashing)
        {
            TickDash(dt, ref velocity);
        }
        else
        {
            // Wall contact must be evaluated before jump resolution so a
            // buffered jump press can be consumed as a wall jump.
            var onWall = !grounded && IsOnWall();
            var wallNormalX = onWall ? Mathf.Sign(GetWallNormal().X) : 0f;
            var pressingIntoWall = onWall && moveAxis != 0f && Mathf.Sign(moveAxis) == -wallNormalX;
            var canWallSlide = onWall && pressingIntoWall && velocity.Y <= 0f &&
                                UnlockedAbilities.HasFlag(AbilityFlags.WallJump) &&
                                !(_isWallJumpLockout && Mathf.Sign(wallNormalX) == _wallJumpLockoutSide);

            if (canWallSlide)
            {
                _doubleJumpUsed = false; // touching a wall refreshes the air jump
                velocity.Y = Mathf.Max(velocity.Y - _gravity * dt, -WallSlideSpeed);
                CurrentState = MovementState.WallSlide;

                if (_jumpBufferTimer > 0f)
                {
                    PerformWallJump(wallNormalX, ref velocity);
                }
            }
            else
            {
                // Grounded / double jump resolution.
                if (_jumpBufferTimer > 0f && (grounded || _coyoteTimer > 0f))
                {
                    velocity.Y = _jumpVelocity;
                    _jumpBufferTimer = 0f;
                    _coyoteTimer = 0f;
                    _doubleJumpUsed = false;
                    CurrentState = MovementState.Jump;
                    EventBus.Instance?.EmitJumped();
                }
                else if (_jumpBufferTimer > 0f && !grounded && !_doubleJumpUsed &&
                         UnlockedAbilities.HasFlag(AbilityFlags.DoubleJump))
                {
                    velocity.Y = _doubleJumpVelocity;
                    _jumpBufferTimer = 0f;
                    _doubleJumpUsed = true;
                    CurrentState = MovementState.DoubleJump;
                    EventBus.Instance?.EmitDoubleJumped();
                }
                else if (Input.IsActionJustReleased("jump") && velocity.Y > 0f &&
                         (CurrentState == MovementState.Jump || CurrentState == MovementState.DoubleJump))
                {
                    velocity.Y *= JumpCutMultiplier;
                }

                // Dash trigger (only outside of wall-slide/jump this frame).
                if (_dashBufferTimer > 0f && UnlockedAbilities.HasFlag(AbilityFlags.Dash) &&
                    _dashCooldownTimer <= 0f && CurrentState != MovementState.WallJump)
                {
                    StartDash(moveAxis, ref velocity);
                }
                else
                {
                    if (!grounded)
                        velocity.Y -= CurrentGravity(velocity.Y) * dt;
                    velocity = ApplyGroundOrAirFriction(velocity, moveAxis, grounded, dt);
                }
            }

            velocity.Y = Mathf.Max(velocity.Y, -MaxFallSpeed);
        }

        if (moveAxis != 0f && !_isDashing)
            FacingSign = moveAxis > 0f ? 1 : -1;

        Velocity = velocity;
        MoveAndSlide();
        LockPlaneAxes();

        ResolveFacingRotation();
        ResolvePostMoveState(grounded, moveAxis);

        if (!IsOnFloor()) _fallSpeedOnLanding = Mathf.Max(0f, -Velocity.Y);
        _wasOnFloor = IsOnFloor();
    }

    private float CurrentGravity(float verticalVelocity) =>
        verticalVelocity < 0f ? _gravity * FallGravityMultiplier : _gravity;

    private Vector3 ApplyGroundOrAirFriction(Vector3 velocity, float moveAxis, bool grounded, float dt)
    {
        var targetX = moveAxis * MoveSpeed;
        var rate = grounded ? GroundAcceleration : GroundAcceleration * AirControlMultiplier;
        if (moveAxis == 0f)
            rate = grounded ? GroundFriction : GroundFriction * AirControlMultiplier;

        velocity.X = Mathf.MoveToward(velocity.X, targetX, rate * dt);
        return velocity;
    }

    private void TickTimers(float dt)
    {
        if (_coyoteTimer > 0f) _coyoteTimer -= dt;
        if (_jumpBufferTimer > 0f) _jumpBufferTimer -= dt;
        if (_dashBufferTimer > 0f) _dashBufferTimer -= dt;
        if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
        if (_dashIFrameTimer > 0f) _dashIFrameTimer -= dt;
        if (_hurtInvulnTimer > 0f) _hurtInvulnTimer -= dt;

        if (_isWallJumpLockout)
        {
            _wallJumpLockoutTimer -= dt;
            if (_wallJumpLockoutTimer <= 0f)
                _isWallJumpLockout = false;
        }

        if (_isHurt)
        {
            _hurtStunTimer -= dt;
            if (_hurtStunTimer <= 0f)
                _isHurt = false;
        }
    }

    private void StartDash(float moveAxis, ref Vector3 velocity)
    {
        var dirX = moveAxis != 0f ? Mathf.Sign(moveAxis) : FacingSign;
        _dashDirection = new Vector3(dirX, 0f, 0f);

        _isDashing = true;
        _dashTimer = DashDuration;
        _dashCooldownTimer = DashCooldown;
        _dashIFrameTimer = DashIFrameDuration;
        _dashBufferTimer = 0f;

        var dashSpeed = DashDuration > 0f ? DashDistance / DashDuration : DashDistance;
        velocity = _dashDirection * dashSpeed;

        CurrentState = MovementState.Dash;
        EventBus.Instance?.EmitDashed();

        // A streak thrown BEHIND the dash. It is the only thing on screen that
        // says how long the invulnerable window is, and the window is the whole
        // reason to press the button.
        Combat.ImpactBurst.Spawn(
            GetParent(),
            GlobalPosition + new Vector3(-FacingSign * 0.3f, -0.35f, 0f),
            new Color(0.78f, 0.86f, 0.95f),
            reach: 1.1f,
            life: 0.22f,
            flatten: 0.3f,
            drift: -FacingSign * 1.1f);
    }

    private void TickDash(float dt, ref Vector3 velocity)
    {
        _dashTimer -= dt;
        var dashSpeed = DashDuration > 0f ? DashDistance / DashDuration : DashDistance;
        velocity = _dashDirection * dashSpeed;

        if (_dashTimer <= 0f)
        {
            _isDashing = false;
            // Preserve dash momentum into the following fall/run rather than
            // hard-stopping; scale down so it doesn't feel like an infinite slide.
            velocity.X = _dashDirection.X * MoveSpeed;
        }
    }

    private void PerformWallJump(float wallNormalX, ref Vector3 velocity)
    {
        var away = -Mathf.Sign(wallNormalX);
        var angleRad = Mathf.DegToRad(WallJumpAngleDegrees);
        velocity.X = away * Mathf.Cos(angleRad) * WallJumpImpulse;
        velocity.Y = Mathf.Sin(angleRad) * WallJumpImpulse;

        _jumpBufferTimer = 0f;
        _doubleJumpUsed = false;
        _isWallJumpLockout = true;
        _wallJumpLockoutTimer = WallJumpLockoutDuration;
        _wallJumpLockoutSide = (int)Mathf.Sign(wallNormalX);
        FacingSign = away;

        CurrentState = MovementState.WallJump;
        EventBus.Instance?.EmitWallJumped();
    }

    private void LockPlaneAxes()
    {
        var pos = GlobalPosition;
        if (!_isDead && GlobalPosition.Y < FallDeathY)
        {
            _isDead = true;
            CurrentHealth = 0;
            Velocity = Vector3.Zero;
            EventBus.Instance?.EmitPlayerDied();
        }

        if (!Mathf.IsEqualApprox(pos.Z, _lockedZ))
        {
            pos.Z = _lockedZ;
            GlobalPosition = pos;
        }

        var rot = RotationDegrees;
        if (rot.X != 0f || rot.Z != 0f)
        {
            rot.X = 0f;
            rot.Z = 0f;
            RotationDegrees = rot;
        }
    }

    private void ResolveFacingRotation()
    {
        var rot = RotationDegrees;
        rot.Y = FacingSign > 0 ? 0f : 180f;
        RotationDegrees = rot;
    }

    private void ResolvePostMoveState(bool wasGroundedThisFrame, float moveAxis)
    {
        var nowGrounded = IsOnFloor();

        if (_isHurt)
        {
            CurrentState = MovementState.Hurt;
            return;
        }

        if (_isDashing)
            return; // state already set to Dash by StartDash

        if (nowGrounded)
        {
            if (!_wasOnFloor)
            {
                EventBus.Instance?.EmitLanded();

                // Dust, and only for a landing worth marking. Every step off a
                // 20cm ledge raising a cloud is worse than no dust at all: the
                // effect stops meaning "that was a drop" and starts meaning
                // "you touched the ground". The threshold is a fall speed, not
                // a height, because that is what the impact actually is.
                if (_fallSpeedOnLanding > DustLandingSpeed)
                    Combat.ImpactBurst.Spawn(
                        GetParent(),
                        GlobalPosition + new Vector3(0f, -0.75f, 0f),
                        new Color(0.72f, 0.66f, 0.56f),
                        reach: 0.55f + Mathf.Min(_fallSpeedOnLanding, 22f) * 0.035f,
                        life: 0.30f,
                        flatten: 0.15f);
            }

            _doubleJumpUsed = false;

            if (CurrentState != MovementState.Dash)
                CurrentState = Mathf.Abs(moveAxis) > 0.01f ? MovementState.Run : MovementState.Idle;
            return;
        }

        // A wall slide that has run out of wall is a fall. The block below
        // exists to protect a state set earlier in the same frame, and it
        // cannot tell that from a state left over from the frame before -- so
        // a player who slid off the BOTTOM of a shaft kept the wall-slide
        // state, and with it the pose, all the way down. The clamp that state
        // advertises had already stopped applying: traced at -8.4 m/s
        // accelerating to -15 while CurrentState still read WallSlide, in a
        // state whose entire job is to cap the fall at 3.
        //
        // It went unseen because the shaft used to be in room 1, where it is
        // short enough that the check's window closed before the player
        // reached the bottom. Moving the shaft to a later room is what made
        // the drop long enough to show.
        if (CurrentState == MovementState.WallSlide && !IsOnWall())
        {
            CurrentState = MovementState.Fall;
            return;
        }

        // Airborne: don't stomp a state that was just explicitly set this
        // frame (Jump / DoubleJump / WallJump / WallSlide).
        if (CurrentState is MovementState.Jump or MovementState.DoubleJump or
            MovementState.WallJump or MovementState.WallSlide)
            return;

        CurrentState = MovementState.Fall;
    }

    // ------------------------------------------------------------------
    // IDamageable
    // ------------------------------------------------------------------

    public void TakeDamage(DamageInfo info)
    {
        if (_isDead || IsInvulnerable)
            return;

        // A guard raised in time turns the blow aside. Checked before anything
        // else is applied -- health, knockback, stun and the hurt state all have
        // to be skipped together, and a parry that stopped the damage but kept
        // the stun would read as a parry that did not work.
        _combat ??= GetNodeOrNull<Combat.CombatController>("CombatController");
        var parry = _combat?.TryConsumeParry() ?? Combat.CombatController.ParryResult.None;
        if (parry != Combat.CombatController.ParryResult.None)
        {
            bool perfect = parry == Combat.CombatController.ParryResult.Perfect;

            // An ordinary parry still shoves: it costs ground, which is what
            // makes the perfect one worth aiming for.
            if (!perfect)
            {
                var shove = Velocity;
                shove.X = Mathf.Sign(GlobalPosition.X - info.SourcePosition.X) * HurtKnockbackHorizontal * 0.35f;
                Velocity = shove;
            }

            EventBus.Instance?.EmitParried(perfect, GlobalPosition);

            // Gold for a perfect one, dull steel for an ordinary block. Parented
            // to the parent rather than to the player, so the burst stays where
            // the blow was turned aside instead of riding along afterwards.
            Combat.ImpactBurst.Spawn(
                GetParent(),
                GlobalPosition + new Vector3(FacingSign * 0.5f, 0.2f, 0f),
                perfect ? new Color(1f, 0.85f, 0.35f) : new Color(0.72f, 0.76f, 0.82f),
                reach: perfect ? 1.15f : 0.6f,
                life: perfect ? 0.32f : 0.20f);
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - info.Amount);

        var knockDir = Mathf.Sign(GlobalPosition.X - info.SourcePosition.X);
        if (knockDir == 0f) knockDir = -FacingSign;

        var velocity = Velocity;
        velocity.X = knockDir * HurtKnockbackHorizontal;
        velocity.Y = HurtKnockbackVertical;
        velocity.Z = 0f;
        Velocity = velocity;

        _isDashing = false;
        _isHurt = true;
        _hurtStunTimer = HurtStunDuration;
        _hurtInvulnTimer = HurtInvulnerabilityDuration;
        CurrentState = MovementState.Hurt;

        EventBus.Instance?.EmitPlayerDamaged(info);
        EventBus.Instance?.EmitPlayerHealthChanged(CurrentHealth, MaxHealth);

        if (CurrentHealth <= 0)
        {
            _isDead = true;
            EventBus.Instance?.EmitPlayerDied();
        }
    }

    /// <summary>
    /// Puts the player back in a playable state after a death. Save calls this
    /// (by name, so Save keeps no compile-time dependency on PlayerCamera)
    /// right after moving the body to the last checkpoint — without it the
    /// player is teleported but stays dead, velocity and all, and the run
    /// cannot continue.
    /// </summary>
    private void BroadcastHealth() =>
        EventBus.Instance?.EmitPlayerHealthChanged(CurrentHealth, MaxHealth);

    /// <summary>
    /// Fallback destination when nothing else knows where to put the player —
    /// where this controller started the level.
    /// </summary>
    public void ReturnToSpawn()
    {
        GlobalPosition = _spawnPosition;
        Velocity = Vector3.Zero;
    }

    /// Wipes progression as well as state. Respawn() is for dying mid-run and
    /// deliberately keeps what the player earned; this is for starting over.
    public void ResetForNewRun()
    {
        UnlockedAbilities = AbilityFlags.None;
        Respawn();
        ReturnToSpawn();
    }

    public void Respawn()
    {
        _isDead = false;
        _isHurt = false;
        _hurtStunTimer = 0f;
        _hurtInvulnTimer = 0f;
        CurrentHealth = MaxHealth;
        Velocity = Vector3.Zero;
        CurrentState = MovementState.Idle;
        EventBus.Instance?.EmitPlayerHealthChanged(CurrentHealth, MaxHealth);
    }
}
