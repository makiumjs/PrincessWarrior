using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The Emberhusk — fourteen health, and the only enemy in the game where it
/// matters WHERE you kill it.
///
/// It is a rusher: fast, fragile, with the shortest tell in the branch. Its
/// swing is almost incidental. What it actually does is die -- 0.35 seconds
/// after the killing blow it detonates, and it leaves a fire on the floor for
/// as long as its own corpse lingers.
///
/// This is the type that finally gives the game's most decorative number a job.
/// EnemyController.OnParried multiplies parry knockback by 1.4x on the gold
/// channel, and until now nothing in the game made the DISTANCE a body flies
/// worth anything -- it was a flourish. Here it is a tactic:
///
///   light attack  (knockback 3.0)   -> it dies on top of you
///   heavy attack  (knockback 6.0)   -> right at the edge of the 3m blast
///   charged heavy (6.0 x 1.8 = 10.8) -> safe, and costs 450ms of charge
///   perfect parry on gold (5.5 x 1.4 = 7.7 m/s + 3 up) -> safe, and free
///
/// Its native channel is white, so that last line only exists once the room's
/// heat has promoted it. The Crucible makes the answer better at the same
/// moment it makes the room worse, which is the whole branch in one enemy.
///
/// Never place one alone. The decision is "in what order, and in which
/// direction", and with a single husk there is no decision.
/// </summary>
[GlobalClass]
public partial class Emberhusk : EnemyController
{
    /// The fuse. Long enough to read the flash and leave, short enough that
    /// walking away is a choice made before the kill rather than after it.
    [Export] public float FuseSeconds { get; set; } = 0.35f;

    [Export] public float BlastRadius { get; set; } = 3.0f;
    [Export] public int BlastDamage { get; set; } = 22;
    [Export] public float BlastKnockback { get; set; } = 7.0f;

    [Export] public float PoolRadius { get; set; } = 2.4f;
    [Export] public float PoolSeconds { get; set; } = 3.5f;
    [Export] public int PoolDamagePerTick { get; set; } = 5;
    [Export] public float PoolTickSeconds { get; set; } = 0.5f;

    /// Paid whether the kill was clean or not. Killing it well spares you the
    /// damage and the ground; nothing spares you the heat, because a husk that
    /// went off is a husk that went off.
    [Export] public float HeatOnDetonation { get; set; } = 10f;

    private float _fuse;
    private bool _fuseLit;
    private bool _detonated;

    public override void _Ready()
    {
        MaxHealth = 14;             // two lights (10+12) or a single heavy (25)
        PatrolSpeed = 2.60f;
        ChaseSpeed = 6.20f;         // under the player's 8.0: outrunnable, not ignorable
        DetectionRadius = 9f;
        LoseSightRadius = 15f;
        AttackRange = 1.20f;
        AttackWindupTime = 0.28f;   // the shortest in the branch: a proximity threat
        AttackRecoveryTime = 0.20f;
        AttackCooldown = 1.10f;
        AttackDamage = 9;           // the swing is not the point
        StaggerDuration = 0.25f;

        base._Ready();
    }

    // The telegraph is left at the base class's StandardWhite on purpose and is
    // not overridden. Everything else in the Crucible asks the player to read a
    // channel; this one is too fast to read and is meant to be answered by
    // position instead. Its gold strikes come from the room's heat promoting
    // them, never from the type itself -- which is why the 1.4x knockback is a
    // reward the Crucible hands out rather than a property the husk owns.

    /// <summary>
    /// Lights the fuse. base.Die() does the real work -- the death state, the
    /// killing blow's impulse, the EnemyDied broadcast -- and this only starts
    /// a countdown, so the corpse still slides, settles and sinks the way every
    /// other body in the game does.
    /// </summary>
    protected override void Die()
    {
        base.Die();

        if (_fuseLit) return;
        _fuseLit = true;
        _fuse = FuseSeconds;

        // The tell for the fuse, in the one colour the player already reads as
        // "this is about to hurt".
        Combat.ImpactBurst.Spawn(
            GetParent(),
            GlobalPosition + new Vector3(0f, 0.9f, 0f),
            new Color(1f, 0.85f, 0.30f),
            reach: 0.6f,
            life: FuseSeconds,
            flatten: 0.8f);
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);

        if (!_fuseLit || _detonated) return;

        _fuse -= (float)delta;
        if (_fuse > 0f) return;

        Detonate();
    }

    /// <summary>
    /// Runs exactly once. The corpse can be freed by a room rebuild before the
    /// fuse ends, in which case nothing happens at all -- correct, because a
    /// room that no longer exists cannot be denied.
    /// </summary>
    private void Detonate()
    {
        _detonated = true;

        Vector3 at = GlobalPosition;
        var room = GetParent();

        // The heat first, because it is the one effect that lands whether or
        // not there is a room left to put a fire in.
        World.HeatManager.Instance?.AddHeat(HeatOnDetonation);

        Combat.ImpactBurst.Spawn(
            room,
            at + new Vector3(0f, 0.7f, 0f),
            new Color(1f, 0.55f, 0.12f),
            reach: BlastRadius,
            life: 0.36f,
            flatten: 0.6f,
            drift: 0.4f);

        BlastPlayer(at);
        SpawnPool(room, at);
    }

    /// <summary>
    /// The blast goes through the ordinary damage path, unlike the pool it
    /// leaves behind: a detonation IS a blow, so it should respect the mercy
    /// window, throw the player, and read as a hit rather than as a condition.
    /// </summary>
    private void BlastPlayer(Vector3 at)
    {
        var player = GetTree()?.GetFirstNodeInGroup("player") as Node3D;
        if (player == null || !IsInstanceValid(player)) return;

        // Planar distance: Z is locked for everything on the movement plane,
        // and measuring it would only ever add noise.
        var d = player.GlobalPosition - at;
        float planar = Mathf.Sqrt(d.X * d.X + d.Y * d.Y);
        if (planar > BlastRadius) return;

        if (player is not IDamageable damageable) return;

        float dir = d.X >= 0f ? 1f : -1f;
        damageable.TakeDamage(new DamageInfo
        {
            Amount = BlastDamage,
            SourcePosition = at,
            Knockback = new Vector3(dir * BlastKnockback, 4.0f, 0f),
            IsCritical = false,
            Telegraph = AttackTelegraphType.UnparryableRed,
        });
    }

    /// <summary>
    /// Parented to the room rather than to this node, and the reason is the one
    /// the bolt already documents: the corpse is on a 3.5-second clock of its
    /// own and freeing it must not put the fire out.
    /// </summary>
    private void SpawnPool(Node room, Vector3 at)
    {
        if (room == null || !IsInstanceValid(room)) return;

        var pool = new EmberPool
        {
            Name = "EmberPool",
            Radius = PoolRadius,
            Lifetime = PoolSeconds,
            DamagePerTick = PoolDamagePerTick,
            TickSeconds = PoolTickSeconds,
        };
        room.AddChild(pool);
        pool.GlobalPosition = at;
    }
}
