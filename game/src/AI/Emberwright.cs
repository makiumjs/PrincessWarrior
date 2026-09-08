using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The Emberwright — the Crucible's first enemy whose threat is not damage.
///
/// It kneels at an anvil and does not move, does not chase, and will never
/// reach you. What it does is feed the fire: every second it is alive adds to
/// the room's Forge Heat, so a room with one in it is a room on a timer. The
/// roster's four existing types are all answered by getting there and swinging;
/// this one is answered by DECIDING to get there, through everything else in
/// the room, before the heat decides for you.
///
/// It is not defenceless, and its one attack is deliberately the most readable
/// in the game: always CounterGold, always a 0.75s wind-up. That is not
/// generosity. The gold channel is the one a perfect parry staggers, and its
/// AttackRange is 3.2 -- the same 3.2 as EnemyController.ParryStaggerRadius --
/// so the shockwave that keeps you out and the parry that opens it reach
/// exactly as far as each other. The obstacle and its answer are the same size
/// on purpose.
///
/// Killing it is worth more than killing a body: HeatManager pays the ordinary
/// kill drain plus a relief on top, because the thing that was heating the room
/// has stopped.
/// </summary>
[GlobalClass]
public partial class Emberwright : EnemyController
{
    /// Height of the heat column above the anvil. Tall enough to be read from
    /// across an arena, which is the whole job: this enemy has to be picked out
    /// of a crowd and walked toward.
    [Export] public float ColumnHeight { get; set; } = 4.0f;
    [Export] public float ColumnRadius { get; set; } = 0.32f;
    [Export] public Color ColumnColor { get; set; } = new(1f, 0.42f, 0.12f);

    /// The column is also the room's heat gauge, drawn in the world. A player
    /// who has to look at the HUD to know how close the room is to going off is
    /// a player looking away from the fight, so the second reading of the same
    /// number lives on the thing causing it.
    [Export] public float ColumnMinEnergy { get; set; } = 1.2f;
    [Export] public float ColumnMaxEnergy { get; set; } = 5.5f;

    private MeshInstance3D _column;
    private OmniLight3D _columnLight;
    private StandardMaterial3D _columnMaterial;
    private float _columnTime;

    public override void _Ready()
    {
        // Tuned here rather than in the .tscn, the same way CrossbowSentry and
        // Warlock do it: a type spawned from code is the same enemy as one
        // placed in a scene, and a stat block that lives in two places has
        // already started drifting.
        PatrolSpeed = 0f;
        ChaseSpeed = 0f;            // it never comes to you
        AttackRange = 3.2f;         // == ParryStaggerRadius, see the class note
        DetectionRadius = 11f;
        LoseSightRadius = 14f;
        EmberBounty = 10;
        AttackWindupTime = 0.75f;   // the second-longest tell in the game
        AttackRecoveryTime = 0.40f;
        AttackCooldown = 2.40f;
        AttackDamage = 15;
        StaggerDuration = 0.50f;
        MaxHealth = 60;             // a charged heavy (55) plus a light (10)

        base._Ready();

        // The group is the whole contract with HeatManager: it counts live
        // members and pays a relief when one dies. Joined AFTER base._Ready so
        // the node is fully in the tree first.
        AddToGroup(World.HeatManager.HeatSourceGroup);

        BuildHeatColumn();
    }

    /// <summary>
    /// Always gold. The only type in the roster that does not alternate, and
    /// the reason is that its attack is not trying to kill you -- it is the
    /// last obstacle between you and the anvil. A channel the parry can open is
    /// what turns "wait out the cooldown" into "read it and go through it".
    /// </summary>
    protected override void SelectAttackTelegraph()
    {
        CurrentTelegraph = AttackTelegraphType.CounterGold;
    }

    /// <summary>
    /// The one type in the game that the room's heat does not touch, and the
    /// only one that would be destroyed by it.
    ///
    /// Left promotable, this enemy defeats itself: it raises the heat at
    /// +2.4/s, the heat promotes gold to red at Molten, and red bypasses the
    /// parry entirely -- so the thing whose sole counterplay is a gold-channel
    /// parry spends the fight making itself unparryable, and does it fastest
    /// exactly when there are two of them. Measured against the branch's own
    /// numbers, that is about nine seconds after entering the room.
    ///
    /// The rest of the roster keeps the rule. This one is the exception the
    /// seam was added for.
    /// </summary>
    protected override bool AcceptsHeatPromotion => false;

    /// <summary>
    /// A shockwave rather than a swing: same damage, same wind-up, but it lands
    /// on anything inside AttackRange rather than on a target the base class
    /// picked. It is stationary and cannot turn to face a player who walked
    /// past it, so a directional strike would be a free kill from behind.
    /// </summary>
    protected override void ApplyAttackDamage(Node3D target)
    {
        Combat.ImpactBurst.Spawn(
            GetParent(),
            GlobalPosition + new Vector3(0f, 0.6f, 0f),
            new Color(1f, 0.72f, 0.18f),
            reach: AttackRange,
            life: 0.30f,
            flatten: 0.45f);

        if (target == null || !IsInstanceValid(target)) return;
        if (GlobalPosition.DistanceTo(target.GlobalPosition) > AttackRange) return;
        if (target is not IDamageable damageable) return;

        float dir = target.GlobalPosition.X >= GlobalPosition.X ? 1f : -1f;
        damageable.TakeDamage(new DamageInfo
        {
            Amount = AttackDamage,
            SourcePosition = GlobalPosition,
            Knockback = new Vector3(dir * 5.0f, 2.5f, 0f),
            IsCritical = false,
            Telegraph = CurrentTelegraph,
        });
    }

    /// <summary>
    /// The fire goes out with it. Leaving the column burning over a corpse
    /// would say the room is still heating when HeatManager has already stopped
    /// counting it -- and a world that contradicts the bar teaches the player to
    /// distrust both.
    /// </summary>
    protected override void Die()
    {
        base.Die();

        // Guarded: Godot logs an error for leaving a group you are not in, and
        // an error printed during a passing test fails this project's suite.
        if (IsInGroup(World.HeatManager.HeatSourceGroup))
            RemoveFromGroup(World.HeatManager.HeatSourceGroup);

        if (_column != null) _column.Visible = false;
        if (_columnLight != null) _columnLight.Visible = false;
    }

    // -- The column --------------------------------------------------------

    private void BuildHeatColumn()
    {
        _columnMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(ColumnColor.R, ColumnColor.G, ColumnColor.B, 0.30f),
            EmissionEnabled = true,
            Emission = ColumnColor,
            EmissionEnergyMultiplier = 2.4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // Unshaded and depth-test-writing off so the column reads as light
            // rather than as a plastic tube standing in the room.
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        _column = new MeshInstance3D
        {
            Name = "HeatColumn",
            Mesh = new CylinderMesh
            {
                TopRadius = ColumnRadius * 0.35f,
                BottomRadius = ColumnRadius,
                Height = ColumnHeight,
                RadialSegments = 10,
                Rings = 1,
            },
            MaterialOverride = _columnMaterial,
            Position = new Vector3(0f, ColumnHeight * 0.5f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_column);

        _columnLight = new OmniLight3D
        {
            Name = "HeatColumnLight",
            LightColor = ColumnColor,
            LightEnergy = ColumnMinEnergy,
            OmniRange = 7.5f,
            ShadowEnabled = false,
            Position = new Vector3(0f, 1.1f, 0f),
        };
        AddChild(_columnLight);
    }

    /// _Process rather than _PhysicsProcess: this is decoration, it does not
    /// need the fixed step, and the base class owns the physics tick.
    public override void _Process(double delta)
    {
        if (_column == null || !_column.Visible) return;

        _columnTime += (float)delta;

        // Two motions, deliberately at different rates so the column never
        // settles into a loop the eye can predict: a fast flicker for the fire
        // and a slow swell that follows the room's heat.
        float flicker = 0.88f + 0.12f * Mathf.Sin(_columnTime * 7.3f);
        float energy = Mathf.Lerp(ColumnMinEnergy, ColumnMaxEnergy, RoomHeat01) * flicker;

        _columnLight.LightEnergy = energy;
        _columnMaterial.EmissionEnergyMultiplier = 1.4f + 3.0f * RoomHeat01 * flicker;

        var scale = _column.Scale;
        scale.X = 1f + 0.25f * RoomHeat01 * flicker;
        scale.Z = scale.X;
        _column.Scale = scale;
    }
}
