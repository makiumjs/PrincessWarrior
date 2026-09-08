using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// Interactive monument placed in Room 0 (The Camp).
/// Detects player proximity and triggers the meta-progression RuneForgeModal
/// via EventBus.RuneForgeRequested.
///
/// Built entirely in code with procedural geometry and an Area3D trigger:
/// freed with Room 0 teardown, guaranteeing zero node leaks across room rebuilds.
/// </summary>
public partial class RunestoneShrine : Node3D
{
    private Label3D _prompt;
    private bool _playerNearby;
    private OmniLight3D _light;
    private MeshInstance3D _runeStone;
    private float _time;

    public override void _Ready()
    {
        // 1. Stone altar pillar
        var pillar = new MeshInstance3D
        {
            Name = "Pillar",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.55f,
                BottomRadius = 0.65f,
                Height = 1.3f,
                RadialSegments = 16,
            },
            Position = new Vector3(0f, 0.65f, 0f),
        };
        var stoneMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.22f, 0.23f, 0.26f),
            Roughness = 0.85f,
            Metallic = 0.15f,
        };
        pillar.MaterialOverride = stoneMat;
        AddChild(pillar);

        // 2. Floating glowing rune core on top
        _runeStone = new MeshInstance3D
        {
            Name = "RuneCore",
            Mesh = new BoxMesh
            {
                Size = new Vector3(0.35f, 0.35f, 0.35f),
            },
            Position = new Vector3(0f, 1.7f, 0f),
            RotationDegrees = new Vector3(45f, 45f, 0f),
        };
        var runeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.72f, 0.25f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.72f, 0.25f),
            EmissionEnergyMultiplier = 4.0f,
            Roughness = 0.2f,
        };
        _runeStone.MaterialOverride = runeMat;
        AddChild(_runeStone);

        // 3. Warm ambient golden light
        _light = new OmniLight3D
        {
            Name = "ShrineLight",
            LightColor = new Color(1.0f, 0.78f, 0.35f),
            LightEnergy = 1.8f,
            OmniRange = 4.0f,
            Position = new Vector3(0f, 1.8f, 0f),
        };
        AddChild(_light);

        // 4. In-world interaction prompt
        _prompt = new Label3D
        {
            Name = "Prompt",
            Text = "[E] FORGIA DELLE RUNE",
            FontSize = 26,
            OutlineSize = 6,
            Modulate = new Color(1.0f, 0.9f, 0.5f),
            OutlineModulate = new Color(0.05f, 0.05f, 0.05f, 0.9f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Position = new Vector3(0f, 2.4f, 0f),
            Visible = false,
        };
        AddChild(_prompt);

        // 5. Proximity Area3D
        var area = new Area3D
        {
            Name = "InteractArea",
            CollisionLayer = 0,
            CollisionMask = PhysicsLayers.Player,
        };
        var col = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(3.2f, 2.8f, 3.5f) },
            Position = new Vector3(0f, 1.4f, 0f),
        };
        area.AddChild(col);
        area.BodyEntered += OnBodyEntered;
        area.BodyExited += OnBodyExited;
        AddChild(area);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        // Subtle levitation and rotation of the rune core
        if (_runeStone != null)
        {
            _runeStone.Position = new Vector3(0f, 1.7f + Mathf.Sin(_time * 2.0f) * 0.08f, 0f);
            _runeStone.RotateY((float)delta * 0.9f);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_playerNearby) return;

        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.E)
        {
            EventBus.Instance?.EmitRuneForgeRequested(true);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventJoypadButton joy && joy.Pressed && joy.ButtonIndex == JoyButton.DpadUp)
        {
            EventBus.Instance?.EmitRuneForgeRequested(true);
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = true;
            if (_prompt != null) _prompt.Visible = true;
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body.IsInGroup("player"))
        {
            _playerNearby = false;
            if (_prompt != null) _prompt.Visible = false;
            EventBus.Instance?.EmitRuneForgeRequested(false);
        }
    }
}
