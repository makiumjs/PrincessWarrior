using Godot;

namespace LostCrownlike.World;

/// <summary>
/// Makes a torch read as fire rather than a lamp: the light's energy wanders
/// on layered sine waves plus a little noise, and the warm tint shifts
/// slightly with it. Static point lights are the tell that a "torch" is just
/// geometry with a bulb behind it.
/// </summary>
public partial class TorchFlicker : OmniLight3D
{
    [Export] public float BaseEnergy = 3.4f;
    [Export] public float FlickerDepth = 0.35f;
    [Export] public float FlickerSpeed = 7.0f;

    private float _t;
    private float _phase;

    public override void _Ready()
    {
        // Randomised phase, otherwise every torch in the room pulses in unison
        // and the whole corridor breathes as one — which reads as a lighting
        // bug, not as fire.
        _phase = GD.Randf() * Mathf.Tau;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta * FlickerSpeed;

        // Two incommensurate sines so the pattern never visibly repeats.
        float wobble = Mathf.Sin(_t + _phase) * 0.6f
                     + Mathf.Sin(_t * 1.73f + _phase * 2f) * 0.4f;
        float jitter = (GD.Randf() - 0.5f) * 0.25f;

        LightEnergy = BaseEnergy * (1f + (wobble + jitter) * FlickerDepth);
        LightColor = new Color(1f, 0.72f + wobble * 0.04f, 0.42f + wobble * 0.05f);
    }
}
