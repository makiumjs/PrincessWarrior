using System.Collections.Generic;
using Godot;
using LostCrownlike.Core;

namespace LostCrownlike.World;

/// <summary>
/// The Crucible's floor, told in light.
///
/// A thin emissive skin laid a centimetre over every walkable rect of the room,
/// brightening with the Forge Heat and pulsing once the floor is hot enough to
/// burn. It is the second reading of a number the HUD already draws, and the
/// more important one: a player who has to look at a bar to know how close the
/// room is to going off is a player looking away from the fight.
///
/// It exists because the slag rule is invisible otherwise. At Molten, standing
/// still for two seconds costs health, and a rule the player can feel but not
/// see reads as the game being broken -- the same finding that put arrows on
/// the conveyor floors.
///
/// Pure consumer of the bus, like the HUD. It never asks HeatManager anything;
/// it is told.
/// </summary>
public partial class SlagVeins : Node3D
{
    /// How far above the walking surface the skin sits. The same centimetre the
    /// conveyor arrows use: enough to beat z-fighting, not enough to be a step.
    [Export] public float Lift { get; set; } = 0.012f;

    [Export] public float Depth { get; set; } = 4f;      // matches the floor grid
    [Export] public Color VeinColor { get; set; } = new(1f, 0.34f, 0.10f);

    /// Emission at an empty bar and at a full one. Never zero at the bottom:
    /// the Crucible is a forge even when it is cold, and a floor that goes
    /// completely dark would read as the effect having failed.
    [Export] public float MinEnergy { get; set; } = 0.35f;
    [Export] public float MaxEnergy { get; set; } = 4.20f;

    /// Two hertz, and only from Molten upward. It is not decoration: it is the
    /// announcement that the ground now costs health after two seconds, and it
    /// starts at the exact heat that rule does.
    [Export] public float PulseHz { get; set; } = 2f;

    /// One material for the whole room. Every span shares it, so following the
    /// heat is a single write per frame rather than one per floor tile -- a
    /// Crucible room has forty-odd rects and this runs every frame.
    private readonly StandardMaterial3D _material = new()
    {
        AlbedoColor = new Color(1f, 0.34f, 0.10f, 0.42f),
        EmissionEnabled = true,
        Emission = new Color(1f, 0.34f, 0.10f),
        EmissionEnergyMultiplier = 0.35f,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    private readonly List<MeshInstance3D> _spans = new();
    private float _heat01;
    private int _heatState;
    private float _time;
    private bool _subscribed;

    public override void _Ready()
    {
        if (EventBus.Instance == null) return;
        EventBus.Instance.HeatChanged += OnHeatChanged;
        _subscribed = true;
    }

    public override void _ExitTree()
    {
        if (!_subscribed) return;
        if (EventBus.Instance != null) EventBus.Instance.HeatChanged -= OnHeatChanged;
        _subscribed = false;
    }

    /// <summary>
    /// Lays the skin over one platform rect. Called by the builder once per
    /// rect, after this node is in the tree; the material is a field
    /// initialiser precisely so the call order does not matter.
    /// </summary>
    public void AddSpan(float x0, float x1, float y)
    {
        float width = x1 - x0;
        if (width <= 0.01f) return;

        var span = new MeshInstance3D
        {
            Name = $"Vein{x0:F0}",
            Mesh = new BoxMesh { Size = new Vector3(width, 0.02f, Depth) },
            MaterialOverride = _material,
            Position = new Vector3(x0 + width * 0.5f, y + Lift, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(span);
        _spans.Add(span);
    }

    public int SpanCount => _spans.Count;

    private void OnHeatChanged(float heat01, int state)
    {
        _heat01 = Mathf.Clamp(heat01, 0f, 1f);
        _heatState = state;
    }

    /// _Process, not _PhysicsProcess: this is light, and light does not need
    /// the fixed step.
    public override void _Process(double delta)
    {
        if (_spans.Count == 0) return;

        _time += (float)delta;

        // Molten and Flashover are the two states the slag rule is live in, and
        // they are the two the floor pulses in. Read from the state rather than
        // re-derived from a threshold this file would then own a second copy of.
        bool burning = _heatState >= (int)HeatState.Molten;

        float pulse = burning
            ? 0.70f + 0.30f * Mathf.Sin(_time * Mathf.Tau * PulseHz)
            : 1f;

        _material.EmissionEnergyMultiplier = Mathf.Lerp(MinEnergy, MaxEnergy, _heat01) * pulse;

        // The skin also thickens as it heats. A colour that only brightens
        // saturates and stops reading; an alpha that climbs with it keeps the
        // last stretch of the bar legible on a bright floor.
        _material.AlbedoColor = new Color(
            VeinColor.R, VeinColor.G, VeinColor.B,
            Mathf.Lerp(0.22f, 0.62f, _heat01) * pulse);
    }
}
