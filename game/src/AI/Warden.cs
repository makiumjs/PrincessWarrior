using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The boss of the last room. The only enemy that cannot be out-traded.
///
/// Every other type in the roster is beaten by movement: walk up to the grunt,
/// close on the sentry, wait out the skirmisher, take back the ground the
/// warlock keeps giving up. All four are answered with the same verb -- get
/// there and swing. That makes the parry decorative: it is never the cheapest
/// option, so a player never has to learn it.
///
/// This one is ARMOURED while it is doing anything, and a hit that lands on
/// armour neither hurts it much nor interrupts it. It opens only when a
/// perfect parry staggers it, and then it is fully open for as long as the
/// stagger lasts. So the fight has exactly one grammar: read the wind-up,
/// parry it, and spend the window. Chip damage still exists, deliberately --
/// a player who never lands a parry can still win, in about sixty hits, which
/// is long enough to teach the lesson without being a wall.
/// </summary>
[GlobalClass]
public partial class Warden : EnemyController
{
    [Export] public PackedScene BoltScene;

    /// What a blow does when it lands on armour, regardless of its real value.
    /// Flat rather than a percentage: a percentage would make the charged
    /// heavy the right answer to armour, and the right answer is the parry.
    [Export] public int ChipDamage = 2;

    /// Fraction of health at which it changes its mind about range.
    [Export] public float EnrageAt = 0.5f;

    [Export] public float MuzzleHeight = 1.2f;
    [Export] public float MuzzleOffsetX = 0.8f;

    /// True while a blow would be absorbed. Public because the HUD draws it:
    /// an armoured boss that looks identical to an open one is a fight the
    /// player loses without ever learning why.
    public bool IsArmoured => State != EnemyState.Stagger && State != EnemyState.Dead;

    public bool IsEnraged { get; private set; }

    public float HealthFraction => MaxHealth <= 0 ? 0f : Mathf.Clamp((float)Health / MaxHealth, 0f, 1f);

    private bool _volleyReady;
    private int _lastBroadcastHealth = -1;
    private bool _lastBroadcastArmoured;
    private bool _lastBroadcastAlive;

    public override void _Ready()
    {
        Configure();
        base._Ready();
        AddToGroup("boss");
    }

    /// The stat block, virtual so a lesser one can inherit the FIGHT without
    /// inheriting the numbers. Everything that makes the Warden the Warden --
    /// armour that turns every blow into chip damage, a stagger only a perfect
    /// parry opens, a tell long enough to read -- is behaviour in this class,
    /// not values here.
    protected virtual void Configure()
    {
        PatrolSpeed = 1.6f;
        ChaseSpeed = 3.2f;          // slower than the player: it is met, not fled
        AttackRange = 2.2f;         // longer reach than a grunt's 1.4
        DetectionRadius = 14f;
        LoseSightRadius = 20f;
        AttackWindupTime = 0.85f;   // the longest tell in the game, on purpose:
        AttackRecoveryTime = 0.45f; // the parry window is 0.38s, and a tell the
        AttackCooldown = 1.9f;      // player cannot see is a coin flip
        AttackDamage = 22;
        MaxHealth = 120;
        // Long enough to be worth the parry. A stagger the player cannot reach
        // and spend is not a reward, and this is the fight's only reward.
        StaggerDuration = 1.6f;
        ParryStaggerRadius = 4.0f;
        SteerDeadzone = 0.3f;
    }

    /// Armour. Note what this does NOT do: it never returns 0. A boss that is
    /// literally invulnerable outside one window reads as broken rather than
    /// hard, because nothing on screen distinguishes "absorbed" from "the hit
    /// did not register".
    protected override int AbsorbDamage(int amount)
        => IsArmoured ? Mathf.Min(ChipDamage, amount) : amount;

    /// Chip hits do not interrupt it. This is the half that makes the armour
    /// mean something: without it, mashing would stagger-lock the boss on 2
    /// damage a hit and the parry would still be optional.
    protected override bool StaggersOnHit(DamageInfo info) => false;

    private int _attackSequence;

    protected override void SelectAttackTelegraph()
    {
        _attackSequence++;
        if (IsEnraged)
        {
            // Phase 2 (enraged): 1 = White standard, 2 = Gold counterable smash, 3 = Red unparryable rage sweep
            int mod = _attackSequence % 3;
            CurrentTelegraph = mod switch
            {
                1 => AttackTelegraphType.StandardWhite,
                2 => AttackTelegraphType.CounterGold,
                _ => AttackTelegraphType.UnparryableRed,
            };
        }
        else
        {
            // Phase 1: alternates StandardWhite and CounterGold (the critical opening)
            CurrentTelegraph = (_attackSequence % 2 == 0)
                ? AttackTelegraphType.CounterGold
                : AttackTelegraphType.StandardWhite;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (!IsEnraged && State != EnemyState.Dead && HealthFraction <= EnrageAt)
            Enrage();

        BroadcastIfChanged();
    }

    /// Only on a change. The bar has to follow the armour, which flips several
    /// times a second during a stagger, but emitting every physics tick would
    /// put 60 signals a second on a bus whose other traffic is events.
    private void BroadcastIfChanged()
    {
        bool alive = State != EnemyState.Dead;
        if (Health == _lastBroadcastHealth
            && IsArmoured == _lastBroadcastArmoured
            && alive == _lastBroadcastAlive)
            return;

        _lastBroadcastHealth = Health;
        _lastBroadcastArmoured = IsArmoured;
        _lastBroadcastAlive = alive;
        Core.EventBus.Instance?.EmitBossStateChanged(alive, HealthFraction, IsArmoured, IsEnraged);
    }

    /// Leaving is a state change too, and nothing was saying so. A room rebuild
    /// frees the Warden without killing it -- respawning at a checkpoint after
    /// dying to the boss does exactly that -- so `alive` stayed true with no
    /// boss in the world: the HUD's bar and the boss music both outlived the
    /// fight. Found by the bed check, which read the boss theme still playing
    /// three rooms earlier.
    public override void _ExitTree()
    {
        base._ExitTree();
        if (_lastBroadcastAlive)
        {
            _lastBroadcastAlive = false;
            Core.EventBus.Instance?.EmitBossStateChanged(false, 0f, false, IsEnraged);
        }
    }

    protected override void Die()
    {
        base.Die();
        // Explicit rather than left to the next tick: Die() stops the fight,
        // and a bar that lingers one frame showing an armoured boss at 0 is
        // the kind of detail that reads as a bug.
        Core.EventBus.Instance?.EmitBossStateChanged(false, 0f, false, IsEnraged);
    }

    /// Second phase. The first phase can be survived by standing outside its
    /// reach and waiting, which is not a fight. From here it also throws, so
    /// distance stops being free, and it commits faster.
    private void Enrage()
    {
        IsEnraged = true;
        _volleyReady = true;
        AttackCooldown = 1.25f;
        AttackWindupTime = 0.7f;
        ChaseSpeed = 4.1f;
        AttackRange = 9.0f;   // it can now open at range, via the volley
        TintModel(this, new Color(1f, 0.35f, 0.25f, 0.35f));
        GD.Print("[Warden] enraged");
    }

    /// Melee in reach, a thrown bolt outside it once enraged. Same wind-up
    /// either way, so the tell the player learned in phase one still reads.
    protected override void ApplyAttackDamage(Node3D target)
    {
        float gap = Mathf.Abs(GlobalPosition.X - target.GlobalPosition.X);
        if (gap <= 2.4f || !_volleyReady)
        {
            base.ApplyAttackDamage(target);
            return;
        }

        var scene = BoltScene ?? GD.Load<PackedScene>("res://scenes/enemies/Bolt.tscn");
        if (scene == null) { base.ApplyAttackDamage(target); return; }

        float dir = target.GlobalPosition.X >= GlobalPosition.X ? 1f : -1f;
        var bolt = scene.Instantiate<Projectile>();
        bolt.DirectionX = dir;
        bolt.Damage = Mathf.Max(1, AttackDamage / 2);
        bolt.Speed = 9f;

        var host = GetParent() ?? this;
        host.AddChild(bolt);
        bolt.GlobalPosition = GlobalPosition + new Vector3(dir * MuzzleOffsetX, MuzzleHeight, 0f);
    }
}
