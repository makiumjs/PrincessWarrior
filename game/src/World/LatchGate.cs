using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A barrier that opens when its lever is thrown.
///
/// Deliberately the same SHAPE of thing as ArenaGate -- a tall solid slab with
/// a lit face -- because the player has already learned what an orange wall
/// means: something in this room has to happen before you go on. What changes
/// is what that something is. An arena gate waits for a fight; this one waits
/// for an act.
///
/// It watches the lever's own state rather than subscribing to a signal. The
/// room is rebuilt constantly and a subscription across a rebuild is a
/// dangling handler; this project has already deleted one system that kept
/// receiving events after it should have stopped existing.
/// </summary>
public partial class LatchGate : Node3D
{
    public const string GroupName = "latch_gate";

    [Export] public float Height = 7f;
    /// Which lever. Matched by position rather than by node path so the builder
    /// can place both without wiring, and so a freed lever cannot leave a
    /// dangling reference behind.
    [Export] public float LeverX;

    private bool _open;
    public bool IsOpen => _open;

    public override void _Ready()
    {
        var body = new StaticBody3D
        {
            Name = "GateBody",
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.6f, Height, 4f) },
            Position = new Vector3(0f, Height * 0.5f, 0f),
        });
        AddChild(body);

        AddChild(new MeshInstance3D
        {
            Name = "GateMesh",
            Mesh = new BoxMesh { Size = new Vector3(0.35f, Height, 2.4f) },
            Position = new Vector3(0f, Height * 0.5f, 0f),
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.55f, 0.20f, 0.42f),
                EmissionEnabled = true,
                Emission = new Color(0.95f, 0.5f, 0.15f),
                EmissionEnergyMultiplier = 1.4f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            },
        });

        AddToGroup(GroupName);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_open) return;
        foreach (var n in GetTree().GetNodesInGroup(LeverSwitch.GroupName))
        {
            if (n is not LeverSwitch lever || !lever.IsThrown) continue;
            if (Mathf.Abs(lever.GlobalPosition.X - LeverX) > 1.5f) continue;
            _open = true;
            QueueFree();
            return;
        }
    }
}
