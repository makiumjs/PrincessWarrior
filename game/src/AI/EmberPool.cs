using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// The fire an Emberhusk leaves where it died. Denies a patch of floor for a
/// few seconds, which is the point: the husk's real weapon is not its 22-damage
/// blast, it is that killing one carelessly takes ground away from you for the
/// rest of the fight.
///
/// A Node3D with a distance check rather than an Area3D. Two reasons, and
/// neither is laziness: this thing never moves, so overlap detection buys
/// nothing a distance test does not already give; and an Area3D would have to
/// juggle Monitoring during its own signals, which is the exact edge Projectile
/// documents a workaround for.
///
/// Parented to the ROOM and not to the husk, the same rule the bolt follows: a
/// fire already burning must not vanish because the corpse that started it
/// finished sinking into the floor.
/// </summary>
[GlobalClass]
public partial class EmberPool : Node3D
{
    [Export] public float Radius { get; set; } = 2.4f;

    /// Matched to EnemyController.CorpseLingerSeconds. The fire and the body it
    /// came from leave together, so the floor is clear again at the moment the
    /// thing that lit it is gone.
    [Export] public float Lifetime { get; set; } = 3.5f;

    [Export] public int DamagePerTick { get; set; } = 5;
    [Export] public float TickSeconds { get; set; } = 0.5f;

    /// How far above and below the pool still counts as standing in it. Without
    /// a vertical bound a fire on the floor would burn a player on the walkway
    /// overhead, and this game stacks platforms 1.6m apart.
    [Export] public float VerticalReachUp { get; set; } = 1.8f;
    [Export] public float VerticalReachDown { get; set; } = 0.8f;

    [Export] public Color FireColor { get; set; } = new(1f, 0.42f, 0.10f);

    private float _age;
    private float _tickTimer;
    private MeshInstance3D _disc;
    private OmniLight3D _light;
    private StandardMaterial3D _material;

    public override void _Ready()
    {
        BuildVisual();

        // The first tick is immediate. A pool that waits half a second before
        // it does anything is a pool the player can walk through for free, and
        // the husk's whole threat is the ground it takes.
        _tickTimer = 0f;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _age += dt;

        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }

        Fade();

        _tickTimer -= dt;
        if (_tickTimer > 0f) return;
        _tickTimer = TickSeconds;

        var player = GetTree()?.GetFirstNodeInGroup("player") as PlayerCamera.PlayerController;
        if (player == null) return;
        if (!Covers(player.GlobalPosition)) return;

        // A burn and not a blow. It must not stun, must not knock back, must not
        // be swallowed by the one-second mercy window -- a pool that landed
        // every other tick would deal half the stated rate, and one that stunned
        // would hold the player inside itself.
        //
        // And it must not feed the Forge Heat. Seven ticks over 3.5 seconds at
        // +4 apiece would be +28, where the design assigns the whole detonation
        // +10; the heat manager owns that rule, so the burn goes through it.
        // The fallback is for a scene with no branch running, where the damage
        // still has to land.
        var heat = World.HeatManager.Instance;
        if (heat != null) heat.ApplyEnvironmentalBurn(player, DamagePerTick);
        else player.ApplyBurn(DamagePerTick);
    }

    public bool Covers(Vector3 point)
    {
        float dx = Mathf.Abs(point.X - GlobalPosition.X);
        if (dx > Radius) return false;

        float dy = point.Y - GlobalPosition.Y;
        return dy <= VerticalReachUp && dy >= -VerticalReachDown;
    }

    private void BuildVisual()
    {
        _material = new StandardMaterial3D
        {
            AlbedoColor = new Color(FireColor.R, FireColor.G, FireColor.B, 0.55f),
            EmissionEnabled = true,
            Emission = FireColor,
            EmissionEnergyMultiplier = 3.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        _disc = new MeshInstance3D
        {
            Name = "PoolDisc",
            Mesh = new CylinderMesh
            {
                TopRadius = Radius,
                BottomRadius = Radius,
                Height = 0.12f,
                RadialSegments = 16,
                Rings = 1,
            },
            MaterialOverride = _material,
            Position = new Vector3(0f, 0.06f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_disc);

        _light = new OmniLight3D
        {
            Name = "PoolLight",
            LightColor = FireColor,
            LightEnergy = 3.4f,
            OmniRange = Radius * 2.6f,
            ShadowEnabled = false,
            Position = new Vector3(0f, 0.55f, 0f),
        };
        AddChild(_light);
    }

    /// Dies down rather than blinking out, and flickers while it does. The
    /// player has to be able to judge how much longer the ground is denied
    /// without counting seconds, so the brightness IS the timer.
    private void Fade()
    {
        float remaining = 1f - Mathf.Clamp(_age / Mathf.Max(Lifetime, 0.01f), 0f, 1f);
        float flicker = 0.85f + 0.15f * Mathf.Sin(_age * 11.0f);

        _material.EmissionEnergyMultiplier = 3.2f * remaining * flicker;
        _material.AlbedoColor = new Color(FireColor.R, FireColor.G, FireColor.B, 0.55f * remaining);
        _light.LightEnergy = 3.4f * remaining * flicker;

        var scale = _disc.Scale;
        scale.X = 0.85f + 0.15f * remaining;
        scale.Z = scale.X;
        _disc.Scale = scale;
    }
}
