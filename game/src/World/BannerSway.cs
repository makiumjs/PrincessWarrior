using Godot;

namespace LostCrownlike.World;

/// Gentle pendulum on hanging cloth, so the room is not perfectly still.
public partial class BannerSway : Node3D
{
    [Export] public float AmplitudeDegrees = 2.6f;
    [Export] public float Speed = 1.1f;

    private float _t;
    private float _phase;
    private float _baseZ;

    public override void _Ready()
    {
        _phase = GD.Randf() * Mathf.Tau;
        _baseZ = RotationDegrees.Z;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta * Speed;
        var r = RotationDegrees;
        r.Z = _baseZ + Mathf.Sin(_t + _phase) * AmplitudeDegrees;
        RotationDegrees = r;
    }
}
