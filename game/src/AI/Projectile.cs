using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// A bolt fired by a ranged enemy. Travels along X on the movement plane,
/// damages the first IDamageable it overlaps, and removes itself on hit or on
/// timeout.
///
/// It is an Area3D, not a CharacterBody3D: a bolt should pass through the
/// firer and register on the player without pushing anything, and overlap
/// detection is the whole of its physics. It carries its own lifetime because
/// a projectile fired down a corridor with no walls would otherwise fly
/// forever, and this project has already been bitten once by an entity that
/// fell out of the level and was never cleaned up.
/// </summary>
[GlobalClass]
public partial class Projectile : Area3D
{
    [Export] public float Speed = 11f;
    [Export] public int Damage = 8;
    [Export] public float Lifetime = 2.5f;
    [Export] public float KnockbackStrength = 3f;

    /// +1 or -1. Set by whatever fires it.
    public float DirectionX = 1f;

    /// The arrow, and it used to be a glowing orange box.
    ///
    /// Reported from play as enemies firing beams of light rather than arrows,
    /// which is exactly what a 0.5x0.1x0.1 BoxMesh with emission at 2.5 is. The
    /// art pack ships the real thing.
    ///
    /// Two measurements decide how it is placed, and neither is guessed. The
    /// model is 1.26m long down its own Z (-0.641 to +0.620) and thin in X and
    /// Y, so a +90 degree turn about Y lays it along the travel axis. Which END
    /// leads was read off the vertex buffer rather than eyeballed: the Z- end
    /// carries 48 vertices spread 0.163 wide and the Z+ end 25 spread 0.108 --
    /// fletching is bushy, a point is not -- so the tip is +Z and +90 about Y
    /// sends it in the direction of travel.
    [Export] public float ArrowScale = 0.55f;

    private const string ArrowModel = "res://assets/kaykit/props/arrow_bow.gltf";

    private void BuildArrow()
    {
        var scene = ResourceLoader.Load<PackedScene>(ArrowModel);
        if (scene == null) { GD.PushWarning("Projectile: no arrow model"); return; }
        var arrow = scene.Instantiate<Node3D>();
        arrow.Name = "Arrow";
        arrow.Scale = Vector3.One * ArrowScale;
        // Flipped with the shot rather than left pointing one way: an arrow
        // travelling left tail-first is worse than the box it replaced.
        arrow.RotationDegrees = new Vector3(0f, DirectionX >= 0f ? 90f : -90f, 0f);
        AddChild(arrow);
    }


    private float _age;
    private bool _spent;

    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = PhysicsLayers.Player | PhysicsLayers.World;
        Monitoring = true;
        // After the firer has set DirectionX, which it does before adding this
        // to the tree -- the arrow has to know which way it is pointing.
        BuildArrow();
        BodyEntered += OnBodyEntered;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_spent) return;

        _age += (float)delta;
        if (_age >= Lifetime)
        {
            Despawn();
            return;
        }

        // Position, not velocity: an Area3D has no move_and_slide, and the
        // plane lock that applies to bodies has to be honoured by hand here.
        var p = GlobalPosition;
        GlobalPosition = new Vector3(p.X + DirectionX * Speed * (float)delta, p.Y, 0f);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_spent) return;

        if (body is IDamageable damageable)
        {
            damageable.TakeDamage(new DamageInfo
            {
                Amount = Damage,
                SourcePosition = GlobalPosition,
                Knockback = new Vector3(DirectionX * KnockbackStrength, 1.5f, 0f),
                IsCritical = false,
            });
        }

        Despawn();
    }

    private void Despawn()
    {
        // No "Monitoring = false" here. Godot refuses to change monitoring from
        // inside the signal it is emitting -- "Function blocked during in/out
        // signal" -- and the guard it was meant to provide is already done by
        // _spent, which makes every further overlap this frame a no-op.
        // QueueFree does the rest at the end of the frame.
        _spent = true;
        QueueFree();
    }
}
