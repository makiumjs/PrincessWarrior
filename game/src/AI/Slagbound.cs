using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The Slagbound — the enemy that punishes the answer every other enemy in
/// this game rewards.
///
/// Four types deep, the roster has one verb: walk up and trade hits. This one
/// wears a shell of molten slag, and a sword laid on it does almost nothing and
/// hurts the hand that swung. The shell opens for exactly one thing -- a
/// PERFECT parry, not an ordinary block -- and then stays open for two seconds.
///
/// Where the Warden's armour says "learn the parry or take sixty hits", this
/// says "learn it or bleed": chip damage still exists so a stubborn player can
/// win, but the burn means stubbornness has a running cost rather than just a
/// long one.
///
/// The attack cycle is White, White, Red. Two of three can be parried and the
/// third has to be dashed, so the fight is a question asked on a beat rather
/// than a reaction test -- and under the Crucible's heat the two whites promote
/// to gold, which means the room makes the parry MORE rewarding at exactly the
/// moment it makes everything else worse.
/// </summary>
[GlobalClass]
public partial class Slagbound : EnemyController
{
    /// What a blow does when it lands on the shell. Flat, like the Warden's
    /// ChipDamage and for the same reason: a percentage would make the charged
    /// heavy the right answer to armour, and the right answer is the parry.
    [Export] public int ShellChipDamage { get; set; } = 3;

    /// What hitting the shell costs the player. Not a counter-attack -- no
    /// stun, no knockback, no interruption of whatever they were doing. A tax.
    [Export] public int ShellBurnDamage { get; set; } = 6;

    /// How long the burn waits before it can bite again. This, and not the
    /// player's hurt-invulnerability, is what holds mashing to about six health
    /// a second: routing the burn through the ordinary damage path would have
    /// granted a full second of immunity to everything else in the room as a
    /// REWARD for hitting the shell.
    [Export] public float BurnCooldownSeconds { get; set; } = 1.0f;

    /// How close the player has to be for the shell to have been touched BY
    /// them. AttackRange (2.0) plus a metre of the player's own melee reach.
    [Export] public float BurnReach { get; set; } = 3.0f;

    /// How long a perfect parry keeps it open. Two seconds is the length of a
    /// six-hit combo chain (10+12+14+10+12+14 = 72), which is this type's
    /// entire health pool -- one flawless window is one kill, and a sloppy one
    /// costs a second reading of the cycle.
    [Export] public float ShellOpenSeconds { get; set; } = 2.0f;

    [Export] public Color SealedTint { get; set; } = new(0.95f, 0.35f, 0.10f, 0.42f);

    /// True while the shell is intact. Public because the impact feedback and
    /// any future HUD reading depend on being able to tell the two apart, and
    /// an enemy whose defence is invisible is a fight the player loses without
    /// learning why -- the lesson the Warden's armour already paid for.
    public bool IsSealed => _shellOpenFor <= 0f && State != EnemyState.Dead;

    private float _shellOpenFor;
    private float _burnCooldown;
    private bool _sealedLastFrame = true;
    private bool _parrySubscribed;

    public override void _Ready()
    {
        MaxHealth = 72;
        PatrolSpeed = 1.20f;
        ChaseSpeed = 3.40f;         // slower than the player's 8: it is met, not fled
        DetectionRadius = 10f;
        LoseSightRadius = 16f;      // it does not give up
        AttackRange = 2.00f;
        AttackWindupTime = 0.62f;   // well past the 0.38s parry window: readable
        AttackRecoveryTime = 0.40f;
        AttackCooldown = 1.90f;
        AttackDamage = 18;
        StaggerDuration = 0.90f;
        SteerDeadzone = 0.30f;
        EmberBounty = 10;

        base._Ready();

        // Its own subscription, in addition to the base class's. The base
        // handler is private and answers a different question -- "was I the one
        // that got parried, and should I stagger" -- while this one asks "was
        // that parry good enough to crack the shell". Sharing one handler would
        // have meant making the base class know what a shell is.
        if (EventBus.Instance != null)
        {
            EventBus.Instance.Parried += OnParriedShell;
            _parrySubscribed = true;
        }

        ApplyShellVisual(sealedNow: true);
    }

    public override void _ExitTree()
    {
        base._ExitTree();

        if (!_parrySubscribed) return;
        if (EventBus.Instance != null)
            EventBus.Instance.Parried -= OnParriedShell;
        _parrySubscribed = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        float dt = (float)delta;

        if (_burnCooldown > 0f)
            _burnCooldown = Mathf.Max(0f, _burnCooldown - dt);

        if (_shellOpenFor > 0f)
            _shellOpenFor = Mathf.Max(0f, _shellOpenFor - dt);

        bool sealedNow = IsSealed;
        if (sealedNow != _sealedLastFrame)
        {
            _sealedLastFrame = sealedNow;
            ApplyShellVisual(sealedNow);
        }
    }

    /// <summary>
    /// A perfect parry cracks the shell. Blocked parries do not: they still
    /// spare the player and still cool the room, but the opening is the reward
    /// reserved for the tight window, and handing it to the generous one would
    /// collapse the two back into a single move.
    ///
    /// Range-checked against ParryStaggerRadius, exactly like the base class's
    /// own handler -- Parried is a broadcast carrying a position rather than a
    /// node, so proximity is what stands in for "the one that swung", and a
    /// parry across the room must not open every shell in it.
    /// </summary>
    private void OnParriedShell(bool perfect, Vector3 atPosition)
    {
        if (!perfect || State == EnemyState.Dead) return;
        if (GlobalPosition.DistanceTo(atPosition) > ParryStaggerRadius) return;

        // An unparryable strike was never turned aside; whatever the player
        // parried, it was not this. Same guard the base handler applies, for
        // the same reason -- without it the third beat of the cycle would open
        // the shell it exists to protect.
        if (CurrentTelegraph == AttackTelegraphType.UnparryableRed) return;

        _shellOpenFor = ShellOpenSeconds;
        GD.Print($"[Slagbound] shell cracked for {ShellOpenSeconds:0.0}s");
    }

    /// <summary>
    /// The shell. Note what it does NOT do: it never returns 0, for the reason
    /// the Warden's armour documents -- a hit that registers as nothing is
    /// indistinguishable on screen from a hit that did not register at all.
    ///
    /// The burn rides here rather than in a separate hook because this is the
    /// only place that knows a blow was absorbed, and splitting "what got
    /// through" from "what it cost" across two methods is how the two drift
    /// out of agreement.
    /// </summary>
    protected override int AbsorbDamage(int amount)
    {
        if (!IsSealed) return amount;

        BurnAttacker();
        return Mathf.Min(ShellChipDamage, amount);
    }

    /// <summary>
    /// Chip hits do not interrupt it while the shell holds -- the same half of
    /// the rule that makes the Warden's armour mean anything. Without it, a
    /// player mashing for 3 damage a swing would stagger-lock the thing and the
    /// parry would stay optional. Once the shell is open it staggers like
    /// anything else, because the open window is supposed to feel like a
    /// different fight.
    /// </summary>
    protected override bool StaggersOnHit(DamageInfo info) => !IsSealed;

    /// <summary>
    /// White, White, Red, on repeat. AttackSequence is incremented by the base
    /// class the instant the attack state is entered, so the first swing is 1
    /// and the red one is every third. The Crucible's heat then promotes this
    /// result, never replaces it: at Molten the two whites become gold and the
    /// red stays red.
    /// </summary>
    protected override void SelectAttackTelegraph()
    {
        CurrentTelegraph = (AttackSequence % 3 == 0)
            ? AttackTelegraphType.UnparryableRed
            : AttackTelegraphType.StandardWhite;
    }

    // -- Burn --------------------------------------------------------------

    private void BurnAttacker()
    {
        if (_burnCooldown > 0f) return;

        var player = GetTree()?.GetFirstNodeInGroup("player") as PlayerCamera.PlayerController;
        if (player == null) return;

        // The shell burns whoever touched it, and AbsorbDamage cannot say who
        // that was -- it is handed an amount and nothing else. Without a range
        // check the burn fires on ANY damage this thing takes, and spikes and
        // sweeping blades damage enemies too: a Slagbound patrolling onto a
        // spike run would chip for 3 a tick and burn the player, from across
        // the room, for more than their whole health bar. The distance stands
        // in for attribution, the same way ParryStaggerRadius does for a parry.
        if (GlobalPosition.DistanceTo(player.GlobalPosition) > BurnReach) return;

        _burnCooldown = BurnCooldownSeconds;

        // A burn and not a blow: no stun, no knockback, no Hurt state -- and no
        // +4 heat. Hitting a hot shell is a condition of the choice the player
        // made, not news about how the fight is going. Routed through the heat
        // manager when there is one so the rule has a single definition; the
        // fallback keeps the damage landing in a test scene that has no branch.
        var heat = World.HeatManager.Instance;
        if (heat != null) heat.ApplyEnvironmentalBurn(player, ShellBurnDamage);
        else player.ApplyBurn(ShellBurnDamage);

        Combat.ImpactBurst.Spawn(
            GetParent(),
            GlobalPosition + new Vector3(0f, 1.0f, 0f),
            new Color(1f, 0.55f, 0.15f),
            reach: 0.7f,
            life: 0.22f);
    }

    // -- Visual ------------------------------------------------------------

    /// Sealed reads as molten, open reads as ordinary. The overlay is lit
    /// rather than unshaded, the correction this project already made once for
    /// the sentry's tint: an unshaded overlay identifies the state and throws
    /// away the torch light that says where the thing is standing.
    private void ApplyShellVisual(bool sealedNow)
    {
        TintModel(this, sealedNow ? SealedTint : new Color(0f, 0f, 0f, 0f));
    }
}
