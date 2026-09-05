using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.AI;

/// <summary>
/// TEST-SCENE ONLY. A minimal CharacterBody3D in the "player" group so
/// EnemyController's patrol/chase/attack states are exercisable without
/// depending on the real PlayerController (owned by the PlayerCamera
/// subsystem, built in parallel and not guaranteed to exist yet).
///
/// Not part of the AI subsystem's public contract — do not reference this
/// from anywhere outside scenes/enemies/EnemyTestScene.tscn. It supports
/// left/right movement (A/D or arrow keys) on the X axis plus gravity, and
/// implements IDamageable so BasicMelee's attack has something to hit.
/// </summary>
[GlobalClass]
public partial class PlayerStandIn : CharacterBody3D, IDamageable
{
    [Export] public float MoveSpeed = 5f;
    [Export] public float Gravity = 20f;
    [Export] public int MaxHealth = 100;

    private int _health;
    private float _lockedZ;

    public override void _Ready()
    {
        _health = MaxHealth;
        _lockedZ = GlobalPosition.Z;

        // Engine-level 2.5D plane lock. The manual Z/rotation correction later
        // in _PhysicsProcess stays as a backstop, but locking the axes here
        // stops the solver from ever generating off-plane motion in the first
        // place, instead of fixing it up after the fact.
        AxisLockLinearZ = true;
        AxisLockAngularX = true;
        AxisLockAngularY = true;
        CollisionLayer = PhysicsLayers.Player;
        CollisionMask = PhysicsLayers.World;
        AddToGroup("player");
    }

    public override void _PhysicsProcess(double delta)
    {
        var dt = (float)delta;
        var velocity = Velocity;

        var input = Input.GetAxis("ui_left", "ui_right");
        velocity.X = input * MoveSpeed;

        if (!IsOnFloor())
            velocity.Y -= Gravity * dt;
        else if (velocity.Y < 0f)
            velocity.Y = 0f;

        velocity.Z = 0f;
        Velocity = velocity;
        MoveAndSlide();

        var pos = GlobalPosition;
        if (!Mathf.IsEqualApprox(pos.Z, _lockedZ))
        {
            pos.Z = _lockedZ;
            GlobalPosition = pos;
        }
    }

    public void TakeDamage(DamageInfo info)
    {
        _health = Mathf.Max(0, _health - info.Amount);
        GD.Print($"[PlayerStandIn] took {info.Amount} damage, health now {_health}");
    }
}
