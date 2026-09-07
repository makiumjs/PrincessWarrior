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
/// It is a PENDULUM, not a slider, and the second version is the one that
/// reads. A blade translating sideways at chest height has no tell: it is at a
/// place, then it is at another place. An arc announces itself -- the swing
/// carries the eye ahead of the blade, and the pause at the top of each side is
/// where a player decides to go. It also lets the prop be a weapon instead of
/// scenery: the first version was a barrel on its side, because the pack ships
/// no blade, and it was reported from play as exactly that -- a barrel.
///
/// The pack does ship an axe. It hangs from the pivot rather than standing on
/// the floor: its handle runs up +Y from a grip at the origin, so a half turn
/// about Z points it down and the head, 0.59 off the shaft in -X, becomes the
/// weight at the end of the arm.
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
    /// Half the arc, in degrees. With the arm below it this puts about four and
    /// a half metres between the two extremes -- wider than a running jump, so
    /// the corridor cannot be crossed by outrunning it.
    [Export] public float ArcDegrees = 70f;
    /// How far the head hangs below the pivot, and the axe is scaled to match.
    /// Sized against the CORRIDOR, not against what looked dramatic: the first
    /// try hung 2.9m from a pivot 3.6m up, which put the shaft through the
    /// ceiling of the band the camera frames and made the head as tall as the
    /// player. A trap you cannot see the top of is a wall with a blade on it.
    [Export] public float ArmLength = 2.4f;
    /// One full there-and-back. Long enough to read, short enough that waiting
    /// out a whole cycle costs more than timing it.
    [Export] public float PeriodSeconds = 2.4f;
    /// Staggers a row of them so a corridor is a rhythm rather than a wall.
    [Export] public float PhaseOffset;
    [Export] public float DamageIntervalSeconds = 0.8f;

    private float _cooldown;
    private float _t;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Hazard;
        CollisionMask = PhysicsLayers.Player | PhysicsLayers.Enemy;
        Monitoring = true;
        _t = PhaseOffset;

        // The hurtbox is only the HEAD, at the end of the arm. A box spanning
        // the whole shaft would kill anyone who walked under the pivot, which
        // is the one place an arc is supposed to be safe.
        _head = new CollisionShape3D
        {
            Name = "Head",
            Shape = new BoxShape3D { Size = new Vector3(1.4f, 1.1f, 2.4f) },
            Position = new Vector3(0f, -ArmLength, 0f),
        };
        AddChild(_head);

        var scene = GD.Load<PackedScene>("res://assets/kaykit/props/axe_1handed.gltf");
        if (scene != null)
        {
            var visual = scene.Instantiate<Node3D>();
            visual.Name = "Blade";
            // Half a turn about Z hangs it: the handle runs up +Y from a grip
            // at the origin, so this points it at the floor.
            visual.RotationDegrees = new Vector3(0f, 0f, 180f);
            visual.Scale = Vector3.One * (ArmLength / 0.97f);
            AddChild(visual);
        }
    }

    private CollisionShape3D _head;

    /// TEST SEAM. Where the dangerous end actually is. The node itself is the
    /// PIVOT and never moves, so a check reading this Area3D's own position
    /// would report a blade that swings as one standing still.
    public Vector3 BladePosition => _head?.GlobalPosition ?? GlobalPosition;

    public override void _PhysicsProcess(double delta)
    {
        _t += (float)delta;
        // A cosine rather than a saw: it slows at each end, which is the part a
        // player reads as "now" -- a constant-speed blade gives no cue about
        // when it is about to turn around, and a pendulum that eases is also
        // what one actually looks like.
        float phase = Mathf.Cos(_t / Mathf.Max(PeriodSeconds, 0.1f) * Mathf.Tau);
        RotationDegrees = new Vector3(0f, 0f, phase * ArcDegrees);

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
