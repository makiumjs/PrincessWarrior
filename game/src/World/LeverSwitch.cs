using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A lever that opens a barrier when it is STRUCK.
///
/// The first thing in this game the player DOES to the world rather than
/// crosses. Everything else is walk-into-it: a checkpoint announces itself when
/// you touch it, a hazard hurts you when you touch it, an arena gate opens when
/// the last enemy dies. Nothing has ever asked the player to act on the level.
///
/// It is hit rather than pressed, and that is a decision about the harness as
/// much as about the game. A "use" key is a verb the traversal bot does not
/// have, and a chunk the bot cannot open is a chunk the metric contract cannot
/// promise -- the same wall a ridden platform runs into. The bot already swings
/// at whatever blocks it, so a lever that answers a sword is a lever the crude
/// witness can throw. It is also the older idea: hitting a switch with a blade
/// is what this genre did before it had a use button.
///
/// It implements IDamageable rather than watching an Area3D, so the ordinary
/// attack hitbox finds it with no new plumbing and no new collision layer.
/// </summary>
public partial class LeverSwitch : StaticBody3D, IDamageable
{
    [Signal] public delegate void ThrownEventHandler();

    public const string GroupName = "lever";

    /// How far the handle tips when thrown. Read from the side, a lever that
    /// only changes colour has not moved.
    [Export] public float ThrowDegrees = 62f;

    public bool IsThrown { get; private set; }

    private Node3D _visual;
    private float _tilt;

    public override void _Ready()
    {
        CollisionLayer = PhysicsLayers.Enemy;   // what the player's hitbox scans
        CollisionMask = 0;

        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.8f, 1.8f, 1.6f) },
            Position = new Vector3(0f, 0.9f, 0f),
        });

        var scene = GD.Load<PackedScene>("res://assets/kaykit/platformer/lever_floor_base_red.gltf");
        if (scene != null)
        {
            _visual = scene.Instantiate<Node3D>();
            _visual.Name = "Handle";
            _visual.Scale = Vector3.One * 1.4f;
            AddChild(_visual);
        }

        AddToGroup(GroupName);
    }

    public void TakeDamage(DamageInfo info)
    {
        if (IsThrown) return;
        IsThrown = true;
        EmitSignal(SignalName.Thrown);
        EventBus.Instance?.EmitLeverThrown(GlobalPosition);
    }

    public override void _Process(double delta)
    {
        if (_visual == null) return;
        // Eased rather than snapped: the throw is the feedback, and a pose that
        // arrives in one frame is a state change nobody sees.
        float target = IsThrown ? ThrowDegrees : 0f;
        _tilt = Mathf.MoveToward(_tilt, target, 220f * (float)delta);
        _visual.RotationDegrees = new Vector3(0f, 0f, _tilt);
    }
}
