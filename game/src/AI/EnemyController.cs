using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// Enemy state machine: Patrol, Chase, Attack, Stagger, Dead.
///
/// Movement plane convention: same as the player — X is horizontal, Y is
/// vertical (gravity axis), Z is frozen at the spawn depth (see
/// ARCHITECTURE.md "coupled trio" / PROMPT.md 2.5D setup). This class does
/// not depend on PlayerController (a different subsystem, built in
/// parallel, may not exist at compile time). The player is detected
/// generically: any Node3D in the "player" group is treated as the target,
/// and damage is dealt via a duck-typed cast to Core.IDamageable — no
/// compile-time reference to the PlayerCamera subsystem.
/// </summary>
[GlobalClass]
public partial class EnemyController : CharacterBody3D, IDamageable
{
    public enum EnemyState
    {
        Patrol,
        Chase,
        Attack,
        Stagger,
        Dead,
    }

    // -- Tunables -----------------------------------------------------
    [Export] public float PatrolSpeed = 2.0f;
    /// How far ahead of itself the enemy probes for floor and walls.
    /// Below this Y the enemy is considered to have fallen out of the level.
    /// A chasing enemy deliberately DOES follow the player off a ledge — that
    /// is the aggressive read — but without this it then falls forever,
    /// accumulating live bodies under the map.
    [Export] public float FallDeathY = -12f;
    [Export] public float LedgeProbeAhead = 0.6f;
    [Export] public float LedgeProbeDepth = 1.2f;
    [Export] public float ChaseSpeed = 4.0f;
    [Export] public float DetectionRadius = 6.0f;
    [Export] public float LoseSightRadius = 9.0f;
    [Export] public float AttackRange = 1.4f;
    [Export] public float AttackWindupTime = 0.35f;
    [Export] public float AttackRecoveryTime = 0.25f;
    [Export] public float AttackCooldown = 1.0f;
    [Export] public int AttackDamage = 10;
    [Export] public float StaggerDuration = 0.4f;
    [Export] public int MaxHealth = 30;
    [Export] public float Gravity = 20f;
    [Export] public float PatrolPointArriveThreshold = 0.3f;
    [Export] public float SteerDeadzone = 0.05f;

    /// <summary>Optional explicit patrol endpoints. If either is unset, the
    /// enemy falls back to +/-3 units on X from its spawn position.</summary>
    [Export] public NodePath PatrolPointAPath { get; set; }
    [Export] public NodePath PatrolPointBPath { get; set; }

    /// <summary>Optional child used purely for facing (flips scale.X). No
    /// procedural art dependency — left null this is a no-op.</summary>
    [Export] public NodePath VisualRootPath { get; set; }

    private Node3D PatrolPointA;
    private Node3D PatrolPointB;
    private Node3D VisualRoot;

    public EnemyState State { get; private set; } = EnemyState.Patrol;

    protected NavigationAgent3D NavAgent;
    protected int Health;

    private float _lockedZ;
    private Vector3 _patrolTargetA;
    private Vector3 _patrolTargetB;

    /// The two ends of this enemy's beat. Exposed read-only so a check can ask
    /// the question that mattered: are they anywhere near the enemy?
    public Vector3 PatrolTargetA => _patrolTargetA;
    public Vector3 PatrolTargetB => _patrolTargetB;
    private bool _headingToB;
    private float _stateTimer;
    private bool _navReady;

    /// Set when _Ready found no patrol markers, so the default patrol has to be
    /// measured from wherever the spawner finally puts this enemy.
    private bool _patrolFallbackPending;
    private float _navRetryTimer;
    private Vector3 _steerTarget;
    private float _attackCooldownRemaining;
    private bool _attackDamageApplied;
    private Vector2 _pendingKnockback;
    private Node3D _cachedPlayer;
    private float _playerCacheTimer;

    public override void _Ready()
    {
        if (Core.EventBus.Instance != null)
            Core.EventBus.Instance.Parried += OnParried;

        Health = MaxHealth;
        _lockedZ = GlobalPosition.Z;

        // Engine-level 2.5D plane lock. The manual Z/rotation correction later
        // in _PhysicsProcess stays as a backstop, but locking the axes here
        // stops the solver from ever generating off-plane motion in the first
        // place, instead of fixing it up after the fact.
        AxisLockLinearZ = true;
        AxisLockAngularX = true;
        AxisLockAngularY = true;

        CollisionLayer = PhysicsLayers.Enemy;
        CollisionMask = PhysicsLayers.World;

        NavAgent = GetNodeOrNull<NavigationAgent3D>("NavigationAgent3D");
        if (NavAgent == null)
        {
            NavAgent = new NavigationAgent3D();
            AddChild(NavAgent);
        }
        NavAgent.PathDesiredDistance = 0.2f;
        NavAgent.TargetDesiredDistance = PatrolPointArriveThreshold;

        PatrolPointA = ResolveNode3D(PatrolPointAPath);
        PatrolPointB = ResolveNode3D(PatrolPointBPath);
        VisualRoot = ResolveNode3D(VisualRootPath);

        // The fallback is deferred to the first physics tick. _Ready runs the
        // instant a node enters the tree, and a spawner that writes the
        // position AFTER AddChild -- which is the obvious way to write it, and
        // what this project's own room builder did -- makes "3 metres either
        // side of me" mean "3 metres either side of the world origin".
        //
        // Every enemy in every generated room therefore patrolled toward x=-3
        // no matter where it stood. Invisible while a room was 64 metres and
        // the enemies were near the start; at 200 metres they set off across
        // the level and three of them walked out of the world in one run.
        if (PatrolPointA != null && PatrolPointB != null)
        {
            _patrolTargetA = PatrolPointA.GlobalPosition;
            _patrolTargetB = PatrolPointB.GlobalPosition;
            _patrolTargetA.Z = _lockedZ;
            _patrolTargetB.Z = _lockedZ;
        }
        else
        {
            _patrolFallbackPending = true;
        }

        // NavigationServer3D only syncs navigation maps at the END of a physics
        // frame. A path queried before that first sync comes back degenerate —
        // GetNextPathPosition() returns our own position — and because
        // NavigationAgent3D recomputes a path only when TargetPosition
        // *changes*, the agent then never moves again, so the target never
        // changes, so the path is never recomputed: a permanent freeze.
        // (Observed: enemy motionless for 200+ frames, dx=0 every tick.)
        // Defer the first target set until the map has actually synced.
        SetupNavWhenReady();
    }

    private async void SetupNavWhenReady()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (!IsInstanceValid(this))
            return;
        _navReady = true;
        SetNavTarget(_headingToB ? _patrolTargetB : _patrolTargetA);
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;

        if (_patrolFallbackPending)
        {
            _patrolFallbackPending = false;
            SetPatrolPoints(GlobalPosition + Vector3.Left * 3f,
                            GlobalPosition + Vector3.Right * 3f);
        }

        if (State != EnemyState.Dead && GlobalPosition.Y < FallDeathY)
        {
            Health = 0;
            Die();
            QueueFree();
            return;
        }

        if (State == EnemyState.Dead)
        {
            // Bodies used to stay forever. Die() deliberately leaves them in the
            // scene so the death animation can play and the corpse can slide to
            // a stop, and nothing freed them afterwards -- a room is not rebuilt
            // while you are fighting in it, so they simply piled up. Measured:
            // twelve kills, twelve bodies, none removed.
            //
            // They sink rather than vanish: a corpse popping out of existence is
            // more distracting than one that was never there.
            _deadFor += dt;
            if (_deadFor > CorpseLingerSeconds)
            {
                float sink = (_deadFor - CorpseLingerSeconds) / Mathf.Max(CorpseSinkSeconds, 0.01f);
                var vis = GetNodeOrNull<Node3D>(VisualRootPath);
                if (vis != null) vis.Position = new Vector3(vis.Position.X, -sink * 1.6f, vis.Position.Z);
                if (sink >= 1f)
                {
                    QueueFree();
                    return;
                }
            }

            // Still integrate: a corpse must carry the killing blow's impulse,
            // decelerate and fall to the ground. Bailing out here left it
            // frozen at the exact instant of death, mid-air if it was airborne.
            var deadVel = TickDead(dt, Velocity);
            if (!IsOnFloor())
                deadVel.Y -= Gravity * dt;
            else if (deadVel.Y < 0f)
                deadVel.Y = 0f;
            deadVel.Z = 0f;
            Velocity = deadVel;
            MoveAndSlide();
            return;
        }

        if (_attackCooldownRemaining > 0f)
            _attackCooldownRemaining = Mathf.Max(0f, _attackCooldownRemaining - dt);

        var player = GetPlayer(dt);

        var velocity = Velocity;
        velocity = State switch
        {
            EnemyState.Patrol => TickPatrol(dt, player, velocity),
            EnemyState.Chase => TickChase(dt, player, velocity),
            EnemyState.Attack => TickAttack(dt, player, velocity),
            EnemyState.Stagger => TickStagger(dt, velocity),
            EnemyState.Dead => TickDead(dt, velocity),
            _ => velocity,
        };

        if (!IsOnFloor())
            velocity.Y -= Gravity * dt;
        else if (velocity.Y < 0f)
            velocity.Y = 0f;

        velocity.Z = 0f;
        Velocity = velocity;
        MoveAndSlide();

        // Hard clamp to the movement plane in case navigation or physics
        // response ever pushes us off the frozen depth.
        var pos = GlobalPosition;
        if (!Mathf.IsEqualApprox(pos.Z, _lockedZ))
        {
            pos.Z = _lockedZ;
            GlobalPosition = pos;
        }

        // Facing normally follows movement. The exception is every state in
        // which the enemy deliberately does not move: TickAttack zeroes X
        // velocity so the enemy commits in place, and a sentry or a warlock at
        // its preferred range never moves at all. Both then kept whatever
        // facing they last had -- a stationary sentry aimed its model away from
        // the player it was shooting at, and a grunt struck backwards at
        // someone standing behind it.
        //
        // Nothing failed, which is why this survived: damage is applied on
        // RADIAL distance and the bolt's direction is computed from the
        // player's position, so the hit always landed. Only the tell was wrong,
        // and a tell pointing the wrong way is worse than no tell at all --
        // it is the one thing the parry has to read.
        float facingHint = velocity.X;
        if (player != null && IsInstanceValid(player)
            && (State == EnemyState.Attack || State == EnemyState.Chase))
            facingHint = player.GlobalPosition.X - GlobalPosition.X;

        UpdateFacing(facingHint);
    }

    /// -1 facing left, +1 facing right, 0 with no visual to read. Public so a
    /// check can assert where the enemy is pointing; the player controller
    /// exposes the same thing under the same name.
    public int FacingSign => VisualRoot == null ? 0 : (VisualRoot.Scale.X < 0f ? -1 : 1);

    // -- IDamageable ----------------------------------------------------

    /// Read-only view of the health the subclass and the checks need. Kept
    /// separate from the protected field so nothing outside the enemy can
    /// write it: a bar or a test that could set health would eventually be the
    /// reason a fight ended.
    public int CurrentHealth => Health;

    public void TakeDamage(DamageInfo info)
    {
        if (State == EnemyState.Dead)
            return;

        int landed = AbsorbDamage(info.Amount);
        Health -= landed;
        _pendingKnockback = new Vector2(info.Knockback.X, info.Knockback.Y);

        // A blow that was mostly absorbed must not look like one that landed.
        // This is the answer to the boss's own design note: on screen,
        // "absorbed" and "the hit did not register" were indistinguishable,
        // and a player cannot learn a rule they cannot see. Dull and small for
        // armour, red and wide for a real hit.
        bool absorbed = landed < info.Amount;
        Combat.ImpactBurst.Spawn(
            GetParent(),
            GlobalPosition + new Vector3(0f, 1f, 0f),
            absorbed ? new Color(0.58f, 0.61f, 0.66f) : new Color(1f, 0.34f, 0.26f),
            reach: absorbed ? 0.4f : 0.85f,
            life: absorbed ? 0.15f : 0.24f);

        if (Health <= 0)
        {
            // Carry the impulse into death. Previously Die() returned before
            // knockback was ever recorded, so the killing blow — the one that
            // should read hardest — moved the enemy not at all.
            Die();
            return;
        }

        if (StaggersOnHit(info))
            TransitionTo(EnemyState.Stagger);
    }

    /// How much of an incoming blow actually lands. Identity for every normal
    /// enemy; the seam exists so a type can be armoured without duplicating
    /// the death and knockback path, which is where the bug would live.
    protected virtual int AbsorbDamage(int amount) => amount;

    /// Whether a non-lethal hit interrupts what the enemy is doing. True for
    /// everything that can be out-traded. A type that answers "keep swinging"
    /// with "no" turns this off, and is then opened some other way.
    protected virtual bool StaggersOnHit(DamageInfo info) => true;

    // -- State ticks ------------------------------------------------------

    private Vector3 TickPatrol(float dt, Node3D player, Vector3 velocity)
    {
        if (player != null && DistanceToPlayerXY(player) <= DetectionRadius)
        {
            TransitionTo(EnemyState.Chase);
            return velocity;
        }

        var target = _headingToB ? _patrolTargetB : _patrolTargetA;
        bool withinThreshold = DistanceXY(GlobalPosition, target) <= PatrolPointArriveThreshold;

        // Turn around at a ledge or a wall, not just at the patrol waypoint.
        // Rooms are now generated with real gaps sized to the player's jump
        // (see World/MicroChunk.cs), so a patroller that only checks its
        // waypoint walks straight off the first platform.
        int facing = _headingToB ? 1 : -1;
        bool blocked = !HasFloorAhead(facing) || IsWallAhead(facing) || IsHazardAhead(facing);

        if (withinThreshold || blocked)
        {
            _headingToB = !_headingToB;
            target = _headingToB ? _patrolTargetB : _patrolTargetA;
            SetNavTarget(target);
        }

        return MoveToward(velocity, PatrolSpeed);
    }

    private Vector3 TickChase(float dt, Node3D player, Vector3 velocity)
    {
        if (player == null || DistanceToPlayerXY(player) > LoseSightRadius)
        {
            TransitionTo(EnemyState.Patrol);
            return velocity;
        }

        var (steerTarget, steerSpeed) = ChaseSteering(player);
        SetNavTarget(steerTarget);

        if (DistanceToPlayerXY(player) <= AttackRange && _attackCooldownRemaining <= 0f)
        {
            TransitionTo(EnemyState.Attack);
            return velocity;
        }

        return MoveToward(velocity, steerSpeed);
    }

    private Vector3 TickAttack(float dt, Node3D player, Vector3 velocity)
    {
        _stateTimer += dt;

        // Freeze horizontal movement during the windup/recovery — the
        // enemy commits to the attack in place.
        velocity.X = 0f;

        if (!_attackDamageApplied && _stateTimer >= AttackWindupTime)
        {
            _attackDamageApplied = true;
            if (player != null && DistanceToPlayerXY(player) <= AttackRange * 1.3f)
                ApplyAttackDamage(player);
        }

        if (_stateTimer >= AttackWindupTime + AttackRecoveryTime)
        {
            _attackCooldownRemaining = AttackCooldown;
            if (player == null || DistanceToPlayerXY(player) > LoseSightRadius)
                TransitionTo(EnemyState.Patrol);
            else
                TransitionTo(EnemyState.Chase);
        }

        return velocity;
    }

    /// Lets a corpse carry its death impulse, decelerate and settle, rather
    /// than freezing mid-air the instant it dies.
    private Vector3 TickDead(float dt, Vector3 velocity)
    {
        velocity.X = Mathf.MoveToward(velocity.X, 0f, 6f * dt);
        return velocity;
    }

    private Vector3 TickStagger(float dt, Vector3 velocity)
    {
        _stateTimer += dt;
        velocity.X = Mathf.MoveToward(velocity.X, 0f, 10f * dt);
        if (_stateTimer <= dt) // first tick after entering: apply knockback once
        {
            velocity.X = _pendingKnockback.X;
            velocity.Y = Mathf.Max(velocity.Y, _pendingKnockback.Y);
        }

        if (_stateTimer >= StaggerDuration)
        {
            var player = GetPlayer(dt);
            TransitionTo(player != null && DistanceToPlayerXY(player) <= LoseSightRadius
                ? EnemyState.Chase
                : EnemyState.Patrol);
        }

        return velocity;
    }

    // -- Helpers ----------------------------------------------------------

    /// A perfect parry punishes the attacker: the blow is turned back on it and
    /// it is left open. Range-limited so a parry answers the enemy that swung,
    /// not everything in the room -- the signal is a broadcast because
    /// DamageInfo carries a source position rather than a source node, so
    /// proximity is what stands in for "the one that hit you".
    [Export] public float ParryStaggerRadius = 3.2f;

    /// How long a body stays before it starts to sink. Long enough to read the
    /// death animation, short enough that a long fight does not fill the room.
    [Export] public float CorpseLingerSeconds = 3.5f;
    [Export] public float CorpseSinkSeconds = 0.9f;

    private float _deadFor;

    /// How long this body has been dead. Exposed for the cleanup check: "a
    /// corpse is still here" and "a corpse has been here too long" are
    /// different findings, and only the second one is a leak.
    public float DeadForSeconds => _deadFor;

    /// Unsubscribing matters here: enemies are freed constantly -- on death, on
    /// every room rebuild -- and a bus holding references to freed nodes throws
    /// on the next emit.
    public override void _ExitTree()
    {
        if (Core.EventBus.Instance != null)
            Core.EventBus.Instance.Parried -= OnParried;
    }

    private void OnParried(bool perfect, Vector3 at)
    {
        if (!perfect || State == EnemyState.Dead) return;
        if (GlobalPosition.DistanceTo(at) > ParryStaggerRadius) return;

        _pendingKnockback = new Vector2(Mathf.Sign(GlobalPosition.X - at.X) * 5.5f, 3f);
        _stateTimer = 0f;
        TransitionTo(EnemyState.Stagger);
    }

    /// Attack timing, for the visual to draw and -- later -- for a parry window
    /// to hang off. An attack the player cannot see coming cannot be answered
    /// with anything but luck, and every enemy here currently winds up with no
    /// distinct pose at all: EnemyState.Attack maps to the same Throw clip for
    /// all three types.
    public bool IsWindingUp => State == EnemyState.Attack && _stateTimer < AttackWindupTime;

    /// 0 at the first frame of the wind-up, 1 at the instant the blow lands.
    public float WindupProgress => State != EnemyState.Attack
        ? 0f
        : Mathf.Clamp(_stateTimer / Mathf.Max(AttackWindupTime, 0.0001f), 0f, 1f);

    /// True from the strike until the enemy recovers.
    public bool IsStriking => State == EnemyState.Attack && _stateTimer >= AttackWindupTime;

    /// <summary>
    /// Where this enemy wants to be while chasing, and how fast it moves to get
    /// there. The default closes on the player.
    ///
    /// This exists because the alternative did not work. Steering runs through
    /// MoveToward, which always travels TOWARD _steerTarget, so a subclass that
    /// only got to choose a speed could close or stand still but never back
    /// away. CrossbowSentry had to be written as a deliberately immobile enemy
    /// for exactly that reason. Returning a target lets a type hold a range
    /// (offset from the player), retreat (past itself), or freeze (its own
    /// position) without reimplementing _PhysicsProcess.
    /// </summary>
    protected virtual (Vector3 Target, float Speed) ChaseSteering(Node3D player)
        => (player.GlobalPosition, ChaseSpeed);

    /// True when the attack cooldown has elapsed. A steering override needs it
    /// to know whether this is the moment to commit or to keep its distance.
    protected bool AttackReady => _attackCooldownRemaining <= 0f;

    /// Whether there is floor just past the leading edge in this direction.
    /// Exposed so a steering override can refuse to reverse off a ledge --
    /// patrol already refuses, and an enemy that backs into a pit reads as
    /// broken rather than as retreating.
    protected bool FloorAhead(int facing) => HasFloorAhead(facing);

    /// Recolours every mesh under this enemy. Enemy types have to be told
    /// apart before they act, and this project ships one character model:
    /// the first ranged enemy was the same KayKit knight as the melee grunt
    /// with a different weapon, which is invisible at the camera's distance.
    /// The overlay is LIT on purpose -- an unshaded one identifies the enemy
    /// but pastes a flat silhouette over it and discards the torch lighting
    /// that says where it is standing.
    protected static void TintModel(Node node, Color tint)
    {
        if (node is MeshInstance3D mi)
        {
            mi.MaterialOverlay = new StandardMaterial3D
            {
                AlbedoColor = tint,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            };
        }
        foreach (var c in node.GetChildren()) TintModel(c, tint);
    }

    /// <summary>Deals damage to the target if it exposes IDamageable. Kept
    /// virtual so subclasses can add hit VFX/SFX hooks around it.</summary>
    protected virtual void ApplyAttackDamage(Node3D target)
    {
        if (target is IDamageable damageable)
        {
            damageable.TakeDamage(new DamageInfo
            {
                Amount = AttackDamage,
                SourcePosition = GlobalPosition,
                Knockback = (target.GlobalPosition - GlobalPosition).Normalized() * 4f,
                IsCritical = false,
            });
        }
    }

    protected virtual void Die()
    {
        State = EnemyState.Dead;
        // Keep the killing blow's impulse instead of snapping to zero; the
        // corpse slides out and settles, which reads as impact.
        Velocity = new Vector3(_pendingKnockback.X, Mathf.Max(0f, _pendingKnockback.Y), 0f);
        CollisionLayer = 0;
        CollisionMask = 0;
        EventBus.Instance?.EmitEnemyDied(this);
    }

    private void TransitionTo(EnemyState next)
    {
        // Re-entering Patrol must re-assert a patrol waypoint. Chase overwrites
        // _steerTarget with the player's position; left stale, that value can
        // already satisfy the arrival threshold on the next Patrol tick, so the
        // enemy stands still again (the round-2 critic flagged this as a way to
        // reintroduce the original freeze).
        if (next == EnemyState.Patrol && State != EnemyState.Patrol)
            SetNavTarget(_headingToB ? _patrolTargetB : _patrolTargetA);

        State = next;
        _stateTimer = 0f;
        _attackDamageApplied = false;
    }

    private Vector3 MoveToward(Vector3 velocity, float speed)
    {
        if (!_navReady)
            return velocity;

        // Steer directly along X toward the current target.
        //
        // Deviation from ARCHITECTURE.md's "navigation via NavigationAgent3D":
        // Godot's 3D navmesh pathing is built for horizontal (XZ) walkable
        // surfaces. This game is a 2.5D side-scroller whose movement plane is
        // X/Y with Z locked, so the baked navmesh never maps the patrol targets
        // onto itself — GetNextPathPosition() returned the agent's own position
        // every single tick, leaving the enemy frozen for 200+ frames (verified
        // by identical frame hashes across a full capture, and by tracing
        // next==pos with dx=0 at pf=40/80/120/160). A navmesh also can't express
        // what a platformer enemy actually needs (ledges, drops, jumps).
        // Direct axis steering is the correct tool here; NavAgent is kept for
        // its arrival bookkeeping and future use, but movement no longer
        // depends on it.
        var dx = _steerTarget.X - GlobalPosition.X;
        // Deadzone, tunable per type. A closer that always wants the player's
        // exact X can use a hair's width, but an enemy holding a range sits at
        // a point the player keeps moving, and too tight a deadzone makes it
        // stutter left-right every frame instead of standing.
        velocity.X = Mathf.Abs(dx) < SteerDeadzone ? 0f : Mathf.Sign(dx) * speed;
        return velocity;
    }

    /// One reusable query object instead of a fresh one per cast.
    ///
    /// PhysicsRayQueryParameters3D.Create allocates a RefCounted Godot object
    /// from C# every time. These probes run every physics frame for every
    /// enemy, and the traversal bot casts five more per frame on top, so a long
    /// run accumulates tens of thousands of them faster than the finaliser
    /// clears them. At shutdown that surfaced as hundreds of
    /// "Leaked unsafe reference to object: <PhysicsRayQueryParameters3D#...>"
    /// followed by a FATAL assertion in csharp_script.cpp -- which is the
    /// intermittent this project could not reproduce for most of its life. It
    /// only appears when a run is long enough, which is why short checks never
    /// saw it.
    private PhysicsRayQueryParameters3D _probe;
    private PhysicsRayQueryParameters3D _hazardProbe;

    private PhysicsRayQueryParameters3D Probe(Vector3 from, Vector3 to)
    {
        _probe ??= new PhysicsRayQueryParameters3D { CollisionMask = PhysicsLayers.World };
        _probe.From = from;
        _probe.To = to;
        return _probe;
    }

    /// Second reusable query, for hazards. Separate from the one above because
    /// a spike trap is an Area3D and the floor probe must keep looking only at
    /// bodies -- and reused for the same reason as the first: allocating one
    /// per frame per enemy is what produced hundreds of leaked references and
    /// a FATAL at shutdown.
    private PhysicsRayQueryParameters3D HazardProbe(Vector3 from, Vector3 to)
    {
        _hazardProbe ??= new PhysicsRayQueryParameters3D
        {
            CollisionMask = PhysicsLayers.Hazard,
            CollideWithAreas = true,
            CollideWithBodies = false,
        };
        _hazardProbe.From = from;
        _hazardProbe.To = to;
        return _hazardProbe;
    }

    /// A spike run just ahead. Patrol refuses it; chase does not.
    ///
    /// Spikes damage enemies as well as the player -- 15 every 0.8s against a
    /// 30hp grunt -- and while the rooms were 64 metres long with one enemy in
    /// them that almost never mattered. At 200 metres with three spike runs and
    /// four to eight enemies, rooms started clearing themselves before the
    /// player arrived: found as a corpse at (98.0, -93.7) in a check that was
    /// only counting bodies.
    ///
    /// The asymmetry is the design, not a shortcut. An enemy that wanders onto
    /// a trap unprompted is the level playing itself; one that follows you onto
    /// a trap because you led it there is a tactic. Chase already commits to
    /// walking off ledges for exactly this reason.
    protected bool IsHazardAhead(int facing)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        var from = GlobalPosition + new Vector3(facing * LedgeProbeAhead, 0.6f, 0f);
        var to = from + new Vector3(facing * LedgeProbeAhead, -1.2f, 0f);
        return space.IntersectRay(HazardProbe(from, to)).Count > 0;
    }

    /// Ray-casts down just past the enemy's leading edge. No hit means the
    /// floor ends there.
    private bool HasFloorAhead(int facing)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return true;

        var from = GlobalPosition + new Vector3(facing * LedgeProbeAhead, 0.2f, 0f);
        var to = from + new Vector3(0f, -LedgeProbeDepth, 0f);
        var query = Probe(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        return space.IntersectRay(query).Count > 0;
    }

    /// Ray-casts forward at chest height so the patroller reverses at a wall
    /// instead of grinding into it for the rest of the patrol leg.
    private bool IsWallAhead(int facing)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return false;

        var from = GlobalPosition + new Vector3(0f, 0.6f, 0f);
        var to = from + new Vector3(facing * LedgeProbeAhead, 0f, 0f);
        var query = Probe(from, to);
        query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        return space.IntersectRay(query).Count > 0;
    }

    /// <summary>
    /// Re-aims the patrol at two world positions. The points are read from the
    /// exported markers once in _Ready and cached, so moving a marker later has
    /// no effect — which left the ledge probe untestable from outside, and an
    /// untestable mechanic is one that breaks silently. Also useful for a
    /// designer retargeting a patrol at runtime.
    /// </summary>
    public void SetPatrolPoints(Vector3 a, Vector3 b, bool headToB = false)
    {
        _patrolTargetA = a;
        _patrolTargetB = b;
        _patrolTargetA.Z = _lockedZ;
        _patrolTargetB.Z = _lockedZ;
        // Which end it walks toward first matters: aiming it at the point it is
        // already next to means it never travels, and any test of what happens
        // along the way silently measures nothing.
        _headingToB = headToB;
        SetNavTarget(_headingToB ? _patrolTargetB : _patrolTargetA);
    }

    private void SetNavTarget(Vector3 target)
    {
        target.Z = _lockedZ;
        _steerTarget = target;
        NavAgent.TargetPosition = target;
    }

    private void UpdateFacing(float velocityX)
    {
        if (VisualRoot == null || Mathf.Abs(velocityX) < 0.01f)
            return;
        var scale = VisualRoot.Scale;
        scale.X = Mathf.Abs(scale.X) * Mathf.Sign(velocityX);
        VisualRoot.Scale = scale;
    }

    private Node3D GetPlayer(float dt)
    {
        _playerCacheTimer -= dt;
        if (_cachedPlayer != null && IsInstanceValid(_cachedPlayer) && _playerCacheTimer > 0f)
            return _cachedPlayer;

        _playerCacheTimer = 0.25f;
        _cachedPlayer = GetTree().GetFirstNodeInGroup("player") as Node3D;
        return _cachedPlayer;
    }

    private float DistanceToPlayerXY(Node3D player) => DistanceXY(GlobalPosition, player.GlobalPosition);

    private static float DistanceXY(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Y - b.Y).Length();

    private Node3D ResolveNode3D(NodePath path)
    {
        if (path == null || path.IsEmpty)
            return null;
        return GetNodeOrNull<Node3D>(path);
    }
}
