using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A blade that crosses a corridor on a cycle, and the first thing in this
/// level that MOVES.
///
/// Every obstacle before it was a static distance sized from PlayerMetrics: a
/// gap is 2.44 metres, a step is 1.88, and the only axis the generator had for
/// making a later room harder was making those numbers bigger. That is why the
/// second half of a run read as the first half louder -- widening a gap is not
/// a new idea, it is the same idea at a different size.
///
/// This one asks a different question. The corridor is flat and free; what
/// costs you is WHEN you enter it. A sweep cannot be sized away, and it cannot
/// be made trivial by owning every ability, which matters now that the player
/// owns them from the first frame.
///
/// It damages rather than blocks, on purpose. A moving platform you must wait
/// for is a rhythm the traversal bot cannot prove -- it holds right and never
/// waits, and a bot taught to wait stops being the deliberately clumsy witness
/// this project relies on. A blade lets a bad player through with a bruise and
/// a good one through clean, so the chunk stays provably crossable while the
/// timing is real.
/// </summary>
public partial class SweepHazard : Area3D
{
    [Export] public int Damage = 12;
    [Export] public float Knockback = 6f;
    /// How far it travels from its anchor, each way.
    [Export] public float Reach = 3.0f;
    /// One full there-and-back. Long enough to read, short enough that waiting
    /// out a whole cycle costs more than timing it.
    [Export] public float PeriodSeconds = 2.4f;
    /// Staggers a row of them so a corridor is a rhythm rather than a wall.
    [Export] public float PhaseOffset;
    [Export] public float DamageIntervalSeconds = 0.8f;

    private float _cooldown;
    private float _t;
    private float _anchorX;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Hazard;
        CollisionMask = PhysicsLayers.Player | PhysicsLayers.Enemy;
        Monitoring = true;
        _anchorX = Position.X;
        _t = PhaseOffset;

        if (GetChildCount() == 0)
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.7f, 3.2f, 2.4f) } });

        // The pack has no blade, so the blade is a barrel on its side: dark,
        // narrow, and unmistakably not scenery once it is moving. Named rather
        // than left anonymous so a check can find it.
        var scene = GD.Load<PackedScene>("res://assets/kaykit/dungeon/barrel_large.gltf");
        if (scene != null)
        {
            var visual = scene.Instantiate<Node3D>();
            visual.Name = "Blade";
            visual.RotationDegrees = new Vector3(0f, 0f, 90f);
            visual.Scale = new Vector3(0.9f, 0.55f, 0.9f);
            visual.Position = new Vector3(0f, -0.2f, 0f);
            AddChild(visual);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta;
        // A cosine rather than a saw: it slows at each end, which is the part a
        // player reads as "now" -- a constant-speed blade gives no cue about
        // when it is about to turn around.
        float phase = Mathf.Cos(_t / Mathf.Max(PeriodSeconds, 0.1f) * Mathf.Tau);
        Position = new Vector3(_anchorX + phase * Reach, Position.Y, Position.Z);

        if (_cooldown > 0f) { _cooldown -= (float)delta; return; }

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
