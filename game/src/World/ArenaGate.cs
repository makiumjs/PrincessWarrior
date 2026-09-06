using Godot;
using LostCrownlike.AI;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// A barrier across the far end of an Arena that lifts when the arena is
/// clear.
///
/// Without it an arena is a decoration: the enemies are placed in the widest
/// flat stretch in the room, which is also the easiest stretch to sprint
/// across, so the fight is entirely optional and the chunk that exists to
/// teach the charge attack teaches nothing. The barrier makes the ground past
/// it something you are given rather than something you take.
///
/// It blocks enemies too, which is deliberate: the fight stays in the room it
/// was built for instead of trailing the player down the corridor.
/// </summary>
public partial class ArenaGate : Node3D
{
    /// Horizontal span whose enemies this gate is waiting on.
    [Export] public float SpanMinX;
    [Export] public float SpanMaxX;

    /// Tall enough that a double jump does not clear it.
    [Export] public float Height = 7f;

    private StaticBody3D _body;
    private bool _open;

    public override void _Ready()
    {
        _body = new StaticBody3D
        {
            Name = "GateBody",
            CollisionLayer = PhysicsLayers.World,
            CollisionMask = 0,
        };
        _body.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(0.6f, Height, 4f) },
            Position = new Vector3(0f, Height * 0.5f, 0f),
        });
        AddChild(_body);

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

    public const string GroupName = "arena_gate";

    public bool IsOpen => _open;

    public override void _PhysicsProcess(double delta)
    {
        if (_open || LiveEnemiesInSpan() > 0) return;

        _open = true;
        QueueFree();
    }

    /// Counts by POSITION rather than by parentage. Enemies are children of the
    /// room, not of the arena, and an enemy that has wandered or been knocked
    /// out of the span is no longer part of this fight -- holding the gate shut
    /// for one standing 40 metres away would read as the gate being broken.
    public int LiveEnemiesInSpan()
    {
        int n = 0;
        var room = GetParent();
        if (room == null) return 0;
        foreach (var child in room.GetChildren())
            if (child is EnemyController e
                && !e.IsQueuedForDeletion()
                && e.State != EnemyController.EnemyState.Dead
                && e.GlobalPosition.X >= SpanMinX && e.GlobalPosition.X <= SpanMaxX)
                n++;
        return n;
    }
}
