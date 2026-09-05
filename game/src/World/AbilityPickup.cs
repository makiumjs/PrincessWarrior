using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// Grants one ability on touch and announces it on the bus, so PlayerCamera
/// ORs the flag in, UI reveals the icon, and Save persists it — all three
/// already listen for AbilityUnlocked, so this node needs no direct reference
/// to any of them.
///
/// This is what makes the map a metroidvania rather than a corridor: the
/// generator puts a pickup immediately before the chunk that requires it, so
/// the obstacle teaches the ability it gates.
/// </summary>
public partial class AbilityPickup : Area3D
{
    [Export] public AbilityFlags Ability = AbilityFlags.Dash;
    [Export] public float BobHeight = 0.25f;
    [Export] public float BobSpeed = 2.2f;
    [Export] public float SpinSpeed = 1.6f;

    private Node3D _visual;
    private float _baseY;
    private float _t;
    private bool _taken;

    public override void _Ready()
    {
        AddToGroup("ability_pickups");
        // Sits on the layer that was declared for it. It was 0, which meant
        // PhysicsLayers.Pickup existed as a constant nothing occupied.
        CollisionLayer = PhysicsLayers.Pickup;
        CollisionMask = PhysicsLayers.Player;
        Monitoring = true;
        _baseY = Position.Y;
        BodyEntered += OnBodyEntered;

        if (GetChildCount() == 0)
        {
            AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.2f, 1.6f, 1.6f) } });
        }

        BuildVisual();
    }

    /// A potion on a crate, not a coloured box.
    ///
    /// The pickup used to be an emissive BoxMesh, which on screen is Godot's
    /// own "no material here" placeholder in three colours -- reported from
    /// play as cubes. A dungeon already has a vocabulary for "something worth
    /// picking up": a bottle on a crate reads as loot at a glance, and the
    /// colour that told you WHICH ability moves to a light so it still carries
    /// at a distance without the mesh looking untextured.
    private void BuildVisual()
    {
        var colour = AbilityLook.ColourFor(Ability);

        var pedestal = Load("box_small");
        if (pedestal != null)
        {
            pedestal.Scale = new Vector3(0.55f, 0.55f, 0.55f);
            pedestal.Position = new Vector3(0f, -0.55f, 0f);
            AddChild(pedestal);
        }

        // The bob and spin belong to the bottle alone; a crate that floats is
        // not scenery, it is a bug.
        _visual = new Node3D { Name = "Bottle" };
        AddChild(_visual);

        var bottle = Load(AbilityLook.PropFor(Ability));
        if (bottle != null)
        {
            bottle.Scale = new Vector3(1.15f, 1.15f, 1.15f);
            _visual.AddChild(bottle);
        }
        else
        {
            // Only if the prop is missing. Kept so a broken asset path shows as
            // a visible marker rather than as an invisible pickup.
            _visual.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.35f, 0.35f) },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = colour, EmissionEnabled = true,
                    Emission = colour, EmissionEnergyMultiplier = 1.4f,
                },
            });
            GD.PushWarning($"AbilityPickup: prop '{AbilityLook.PropFor(Ability)}' missing; using a placeholder cube.");
        }

        AddChild(new OmniLight3D
        {
            LightColor = colour,
            LightEnergy = 1.9f,
            OmniRange = 3.4f,
            Position = new Vector3(0f, 0.35f, 0.2f),
        });
    }

    private static Node3D Load(string prop)
    {
        var scene = GD.Load<PackedScene>($"res://assets/kaykit/dungeon/{prop}.gltf");
        return scene?.Instantiate<Node3D>();
    }

    public override void _Process(double delta)
    {
        if (_taken || _visual == null) return;
        _t += (float)delta;
        _visual.Position = new Vector3(0f, Mathf.Sin(_t * BobSpeed) * BobHeight, 0f);
        _visual.RotateY(SpinSpeed * (float)delta);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_taken) return;
        _taken = true;
        GD.Print($"[Pickup] granted {Ability}");
        EventBus.Instance?.EmitAbilityUnlocked(Ability);
        QueueFree();
    }
}
