using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A floor trap that damages whatever stands on it.
///
/// This is what `PhysicsLayers.Hazard` was reserved for: the constant was
/// declared from the start and nothing in the game ever sat on that layer, so
/// the whole category of environmental danger existed only in an enum.
/// </summary>
public partial class SpikeHazard : Area3D
{
    [Export] public int Damage = 15;
    /// Seconds between ticks, so standing on spikes hurts repeatedly rather
    /// than draining the whole health bar in one frame.
    [Export] public float DamageIntervalSeconds = 0.8f;
    [Export] public float Knockback = 5f;

    private float _cooldown;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Hazard;
        CollisionMask = PhysicsLayers.Player | PhysicsLayers.Enemy;
        Monitoring = true;

        if (GetChildCount() == 0)
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.6f, 0.8f, 3.0f) } });

        var scene = GD.Load<PackedScene>("res://assets/kaykit/dungeon/floor_tile_big_spikes.gltf");
        if (scene != null)
        {
            var visual = scene.Instantiate<Node3D>();
            visual.Position = new Vector3(0f, -0.35f, 0f);
            AddChild(visual);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_cooldown > 0f)
        {
            _cooldown -= (float)delta;
            return;
        }

        foreach (var body in GetOverlappingBodies())
        {
            if (body is not IDamageable damageable) continue;

            float dir = Mathf.Sign(body.GlobalPosition.X - GlobalPosition.X);
            if (dir == 0f) dir = 1f;

            damageable.TakeDamage(new DamageInfo
            {
                Amount = Damage,
                SourcePosition = GlobalPosition,
                Knockback = new Vector3(dir * Knockback, Knockback * 0.5f, 0f),
                IsCritical = false,
            });
            _cooldown = DamageIntervalSeconds;
        }
    }
}
