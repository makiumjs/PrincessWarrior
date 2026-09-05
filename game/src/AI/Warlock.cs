using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// Ranged enemy that HOLDS its range instead of standing in it.
///
/// The roster's problem was never how many types there were, it was that each
/// one had a single answer: walk up and hit the grunt, close on the sentry,
/// wait out the skirmisher. A warlock backs off while you approach and only
/// stops when it has the distance it wants, so closing costs you the ground it
/// keeps taking back.
///
/// This is the first type that uses ChaseSteering for what it was built for --
/// choosing WHERE to be rather than how fast to get there. Skirmisher retreats
/// only while recovering; this holds a standoff continuously.
/// </summary>
[GlobalClass]
public partial class Warlock : EnemyController
{
    [Export] public PackedScene BoltScene;

    /// The distance it wants between itself and the player.
    [Export] public float PreferredRange = 6.5f;

    /// How much closer than that it tolerates before backing away. Without a
    /// band it would jitter forward and back across the exact distance.
    [Export] public float RangeSlack = 1.2f;

    [Export] public float MuzzleHeight = 1.0f;
    [Export] public float MuzzleOffsetX = 0.6f;

    public override void _Ready()
    {
        PatrolSpeed = 1.4f;
        ChaseSpeed = 3.6f;
        AttackRange = 8.5f;
        DetectionRadius = 10f;
        LoseSightRadius = 14f;
        AttackWindupTime = 0.7f;    // the longest tell of the three, and the
        AttackCooldown = 2.0f;      // slowest cadence: it punishes standing still
        MaxHealth = 18;             // the most fragile: reaching it IS the fight
        AttackDamage = 14;          // and the most painful if you do not
        SteerDeadzone = 0.4f;

        base._Ready();
    }

    protected override (Vector3 Target, float Speed) ChaseSteering(Node3D player)
    {
        float gap = Mathf.Abs(GlobalPosition.X - player.GlobalPosition.X);
        int away = GlobalPosition.X < player.GlobalPosition.X ? -1 : 1;

        // Too close: give ground, unless there is none to give. Backing off a
        // ledge would kill it with its own tactic, and patrol already refuses
        // to walk off one.
        if (gap < PreferredRange - RangeSlack)
        {
            if (!FloorAhead(away)) return (GlobalPosition, 0f);
            return (player.GlobalPosition + new Vector3(away * PreferredRange, 0f, 0f), ChaseSpeed);
        }

        // Too far: close, but only to the edge of the band.
        if (gap > PreferredRange + RangeSlack)
            return (player.GlobalPosition + new Vector3(-away * PreferredRange, 0f, 0f), ChaseSpeed);

        // In the band: stand and cast.
        return (GlobalPosition, 0f);
    }

    protected override void ApplyAttackDamage(Node3D target)
    {
        var scene = BoltScene ?? GD.Load<PackedScene>("res://scenes/enemies/Bolt.tscn");
        if (scene == null)
        {
            GD.PushError("Warlock: no bolt scene; the warlock is unarmed");
            return;
        }

        float dir = target.GlobalPosition.X >= GlobalPosition.X ? 1f : -1f;

        var bolt = scene.Instantiate<Projectile>();
        bolt.DirectionX = dir;
        bolt.Damage = AttackDamage;
        // Slower than a crossbow bolt: it is meant to be walked around, not
        // reacted to, which is what makes holding range the threat rather than
        // the projectile itself.
        bolt.Speed = 7.5f;

        var host = GetParent() ?? this;
        host.AddChild(bolt);
        bolt.GlobalPosition = GlobalPosition + new Vector3(dir * MuzzleOffsetX, MuzzleHeight, 0f);
    }
}
