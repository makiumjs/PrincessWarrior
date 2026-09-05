using Godot;

namespace LostCrownlike.AI;

/// <summary>
/// Melee enemy that will not stand and trade. It closes to strike the moment
/// its attack is off cooldown, then backs out of reach while it recovers.
///
/// This is the type that justified the ChaseSteering hook. Every earlier enemy
/// either closed on the player or stood still, because steering could only
/// choose a speed toward a fixed target; retreating needs the target itself.
/// Written alongside the hook rather than after it, so the hook has a consumer
/// from the first commit -- an unused extension point is the same dead
/// parallel system this project already carries one of.
/// </summary>
[GlobalClass]
public partial class Skirmisher : EnemyController
{
    [Export] public float RetreatDistance = 4f;
    [Export] public float RetreatSpeed = 4.6f;
    [Export] public Color SkirmisherTint = new(0.62f, 0.24f, 0.14f, 0.55f);

    public override void _Ready()
    {
        ChaseSpeed = 5.2f;          // commits fast when it decides to strike
        AttackCooldown = 1.5f;      // long enough that the retreat is visible
        AttackWindupTime = 0.22f;
        MaxHealth = 24;
        AttackDamage = 12;

        SteerDeadzone = 0.35f;      // holds its stand-off instead of jittering

        base._Ready();
    }

    // The colour tint is gone. It existed because all three enemies were the
    // same KayKit knight and had to be told apart somehow; they are now
    // different characters carrying different weapons, and an unshaded overlay
    // was flattening the torch lighting to buy readability that the models now
    // provide for free.
    protected override (Vector3 Target, float Speed) ChaseSteering(Node3D player)
    {
        // Cooldown up: commit, and close on the player like anything else.
        if (AttackReady) return (player.GlobalPosition, ChaseSpeed);

        // Recovering: put distance between itself and the player, away from
        // whichever side the player is on.
        int away = GlobalPosition.X < player.GlobalPosition.X ? -1 : 1;

        // But not off a ledge. Backing into a pit reads as a bug, not as a
        // retreat, and the enemy would then die to its own tactic.
        if (!FloorAhead(away)) return (GlobalPosition, 0f);

        // Offset from the PLAYER, not from itself. Retreating relative to its
        // own position recomputes the goal every frame, so the enemy chases a
        // target that keeps moving ahead of it and runs until it loses sight
        // entirely -- measured at 6.90 units of flight for a RetreatDistance of
        // 4. Anchored to the player it backs off to exactly that distance and
        // holds there, which is what SteerDeadzone exists to keep steady, and
        // it stays inside LoseSightRadius so it re-engages when the cooldown
        // comes up instead of wandering off.
        return (player.GlobalPosition + new Vector3(away * RetreatDistance, 0f, 0f), RetreatSpeed);
    }

}
