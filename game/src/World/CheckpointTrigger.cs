using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// Checkpoint/save-shrine trigger: an Area3D that, when the player body
/// overlaps it, reports itself reached via EventBus. LevelManager listens
/// and records the checkpoint's position for its own bookkeeping; Save
/// (a separate subsystem) also listens independently, per ARCHITECTURE.md.
/// </summary>
[GlobalClass]
public partial class CheckpointTrigger : Area3D
{
    [Export] public string CheckpointId = "";

    private const string AssetDir = "res://assets/kaykit/dungeon/";
    private MeshInstance3D _crystal;
    private StandardMaterial3D _crystalMaterial;
    private OmniLight3D _shrineLight;
    private float _time;
    private float _activationFlare;

    public override void _Ready()
    {
        AddToGroup("checkpoints");
        CollisionLayer = 0;
        CollisionMask = PhysicsLayers.Player;
        Monitoring = true;
        BodyEntered += OnBodyEntered;

        BuildShrineVisuals();
        UpdateShrineVisualState(isActive: _activeCheckpointId == CheckpointId);
    }

    private void BuildShrineVisuals()
    {
        // 1. Stone Altar Pedestal
        var columnScene = GD.Load<PackedScene>($"{AssetDir}column.gltf");
        if (columnScene != null)
        {
            var pedestal = columnScene.Instantiate<Node3D>();
            pedestal.Position = new Vector3(0f, -0.5f, -0.6f);
            pedestal.Scale = new Vector3(0.85f, 0.45f, 0.85f);
            AddChild(pedestal);
        }

        // 2. Levitating Resonating Rune Crystal (Diamond orientation)
        _crystalMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.25f, 0.65f, 0.95f, 0.75f),
            EmissionEnabled = true,
            Emission = new Color(0.25f, 0.7f, 0.95f),
            EmissionEnergyMultiplier = 1.2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };

        _crystal = new MeshInstance3D
        {
            Name = "RuneCrystal",
            Position = new Vector3(0f, 0.65f, -0.6f),
            Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.42f, 0.42f) },
            RotationDegrees = new Vector3(45f, 0f, 45f),
            MaterialOverride = _crystalMaterial,
        };
        AddChild(_crystal);

        // 3. Shrine Beacon Light
        _shrineLight = new OmniLight3D
        {
            Name = "ShrineLight",
            Position = new Vector3(0f, 0.75f, -0.4f),
            LightColor = new Color(0.3f, 0.75f, 0.95f),
            LightEnergy = 0.9f,
            OmniRange = 4.5f,
            ShadowEnabled = false,
        };
        AddChild(_shrineLight);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_crystal != null)
        {
            // Smooth levitation bob and axial rotation
            _crystal.Position = new Vector3(0f, 0.65f + Mathf.Sin(_time * 2.4f) * 0.07f, -0.6f);
            _crystal.RotateY((float)delta * 1.5f);
        }

        if (_activationFlare > 0f)
        {
            _activationFlare -= (float)delta * 2.0f;
            if (_shrineLight != null)
                _shrineLight.LightEnergy = 2.4f + Mathf.Max(0f, _activationFlare) * 3.5f;
        }
    }

    private void UpdateShrineVisualState(bool isActive)
    {
        if (_crystalMaterial != null)
        {
            if (isActive)
            {
                _crystalMaterial.AlbedoColor = new Color(0.35f, 0.98f, 0.92f, 0.9f);
                _crystalMaterial.Emission = new Color(0.4f, 1.0f, 0.88f);
                _crystalMaterial.EmissionEnergyMultiplier = 3.4f;
            }
            else
            {
                _crystalMaterial.AlbedoColor = new Color(0.25f, 0.65f, 0.95f, 0.7f);
                _crystalMaterial.Emission = new Color(0.25f, 0.7f, 0.95f);
                _crystalMaterial.EmissionEnergyMultiplier = 1.2f;
            }
        }

        if (_shrineLight != null)
        {
            if (isActive)
            {
                _shrineLight.LightColor = new Color(0.4f, 1.0f, 0.9f);
                _shrineLight.LightEnergy = 2.4f;
                _shrineLight.OmniRange = 7.0f;
            }
            else
            {
                _shrineLight.LightColor = new Color(0.3f, 0.75f, 0.95f);
                _shrineLight.LightEnergy = 0.9f;
                _shrineLight.OmniRange = 4.5f;
            }
        }
    }

    /// The last checkpoint id announced by ANY trigger. Shared, because the
    /// point is "is this already the active checkpoint", not "has this
    /// particular node fired".
    private static string _activeCheckpointId = "";

    private void OnBodyEntered(Node3D body)
    {
        if (string.IsNullOrEmpty(CheckpointId))
            return;

        // Re-entry must not re-announce. The player respawns INSIDE its own
        // checkpoint, so every death produced another BodyEntered, and each one
        // made SaveManager write savegame.tres to disk again and re-show the
        // HUD prompt — which is why the prompt appeared to be stuck on screen
        // permanently. Re-arms as soon as a different checkpoint is reached, so
        // backtracking to an earlier one still works.
        if (_activeCheckpointId == CheckpointId)
            return;

        _activeCheckpointId = CheckpointId;
        _activationFlare = 1.0f;
        UpdateShrineVisualState(isActive: true);
        EventBus.Instance?.EmitCheckpointReached(CheckpointId);
    }

    /// Rooms are rebuilt on transition; the static latch must not survive into
    /// a new room, or its first checkpoint would be silently skipped.
    public override void _ExitTree()
    {
        if (_activeCheckpointId == CheckpointId)
            _activeCheckpointId = "";
    }
}
