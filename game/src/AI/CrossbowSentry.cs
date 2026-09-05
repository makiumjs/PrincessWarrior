using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// Ranged enemy: stands its ground and fires a bolt when the player comes into
/// line. The second enemy type, and it exists to make the movement kit matter
/// — a melee grunt is answered by walking up and trading hits, while a sentry
/// has to be closed on through fire, dashed past, or jumped over.
///
/// It does not move. EnemyController's chase and patrol steering are private,
/// so there is no hook for backing away to keep range; rather than reimplement
/// _PhysicsProcess to fake one, this leans on the hooks that do exist —
/// PatrolSpeed and ChaseSpeed at zero, a long AttackRange, and an override of
/// ApplyAttackDamage. A stationary sentry is an honest enemy; a "kiting
/// archer" that cannot actually kite would not be.
/// </summary>
[GlobalClass]
public partial class CrossbowSentry : EnemyController
{
    [Export] public PackedScene BoltScene;
    [Export] public float MuzzleHeight = 0.9f;
    [Export] public float MuzzleOffsetX = 0.7f;

    public override void _Ready()
    {
        // Tuned here rather than in the .tscn so the type carries its own
        // identity: a sentry spawned from code is the same enemy as one placed
        // in a scene.
        PatrolSpeed = 0f;
        ChaseSpeed = 0f;
        AttackRange = 9f;
        DetectionRadius = 10f;
        LoseSightRadius = 13f;
        AttackWindupTime = 0.55f;   // long enough to read and dodge
        AttackCooldown = 1.6f;
        MaxHealth = 20;             // fragile: the threat is position, not bulk
        AttackDamage = 8;

        base._Ready();
    }

    /// Cold steel-blue, against the melee grunt's warm default. Not decoration:
    /// the whole reason this type exists is that it demands a different answer
    /// from the player, and in the first capture the two enemies were the same
    /// KayKit knight distinguished only by a dagger instead of a sword and a
    /// missing shield -- invisible at the camera's distance. An enemy you
    /// cannot identify before it acts is one you cannot plan around.
    [Export] public Color SentryTint = new(0.16f, 0.34f, 0.70f, 0.62f);


    /// Fires a bolt instead of swinging. The base class calls this at the end
    /// of the windup, having already checked the player is in range.
    // The colour tint is gone. It existed because all three enemies were the
    // same KayKit knight and had to be told apart somehow; they are now
    // different characters carrying different weapons, and an unshaded overlay
    // was flattening the torch lighting to buy readability that the models now
    // provide for free.
    protected override void ApplyAttackDamage(Node3D target)
    {
        var scene = BoltScene ?? GD.Load<PackedScene>("res://scenes/enemies/Bolt.tscn");
        if (scene == null)
        {
            GD.PushError("CrossbowSentry: no bolt scene; the sentry is unarmed");
            return;
        }

        float dir = target.GlobalPosition.X >= GlobalPosition.X ? 1f : -1f;

        var bolt = scene.Instantiate<Projectile>();
        bolt.DirectionX = dir;
        bolt.Damage = AttackDamage;

        // Parented to the room, not to the sentry: a bolt already in flight
        // should not vanish because the enemy that fired it died.
        var host = GetParent() ?? this;
        host.AddChild(bolt);
        bolt.GlobalPosition = GlobalPosition + new Vector3(dir * MuzzleOffsetX, MuzzleHeight, 0f);
    }
}
