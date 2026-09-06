using Godot;

namespace LostCrownlike.Combat;

/// <summary>
/// A short-lived spray of shards at the point of an impact. Frees itself.
///
/// Spawned directly by the code that already knows a blow landed, rather than
/// by a listener node placed in Main.tscn. That is deliberate: a listener is
/// one more thing that can exist, be correct, and be in no scene -- which is
/// how the pause menu spent months invisible. Nothing here can be orphaned,
/// because there is nothing to place.
///
/// Procedural rather than a GpuParticles3D: the whole project builds its
/// meshes this way, six boxes cost nothing, and a particle system would need
/// its own material and process settings to survive a room teardown.
/// </summary>
public partial class ImpactBurst : Node3D
{
    private const int Shards = 6;

    /// What colour this burst is. Public so a check can tell an absorbed blow
    /// apart from a landed one WITHOUT a renderer: the whole point of the two
    /// colours is that they are different, and "different" is a fact about the
    /// data, not about the pixels.
    public Color Tint { get; private set; }

    private float _t;
    private float _life = 0.28f;
    private float _reach = 1f;
    private float _flatten = 1f;
    private float _drift;
    private MeshInstance3D[] _pieces;
    private Vector3[] _dirs;
    private StandardMaterial3D _mat;

    /// <param name="host">Node the burst is parented to. The room, normally,
    /// so a teardown takes it with it.</param>
    /// <param name="flatten">Squashes the fan toward the horizontal. 1 is the
    /// even spray a blow makes; 0.15 is what dust does when something lands on
    /// it -- it goes sideways, not up. The same six shards either way: a second
    /// effect for the cost of an argument.</param>
    /// <param name="drift">Pushes the whole fan one way. A dash leaves its dust
    /// BEHIND it, and a spray that ignores which way you were going reads as a
    /// puff rather than as speed.</param>
    public static void Spawn(Node host, Vector3 at, Color tint, float reach = 1f, float life = 0.28f,
                             float flatten = 1f, float drift = 0f)
    {
        if (host == null || !GodotObject.IsInstanceValid(host)) return;

        var burst = new ImpactBurst
        {
            Name = "ImpactBurst",
            _reach = reach,
            _life = life,
            _flatten = flatten,
            _drift = drift,
        };
        host.AddChild(burst);
        burst.GlobalPosition = at;
        burst.Build(tint);
    }

    private void Build(Color tint)
    {
        Tint = tint;

        // One material for the whole burst: the fade is a single alpha write
        // per frame instead of six, and six materials per hit is the kind of
        // per-frame allocation that only shows up as a frame-time p99.
        _mat = new StandardMaterial3D
        {
            AlbedoColor = tint,
            EmissionEnabled = true,
            Emission = tint,
            EmissionEnergyMultiplier = 2.4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        var mesh = new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.12f) };

        _pieces = new MeshInstance3D[Shards];
        _dirs = new Vector3[Shards];
        for (int i = 0; i < Shards; i++)
        {
            // Fanned on the X/Y plane, because the game is on the X/Y plane:
            // shards thrown along Z would be hidden behind the character by an
            // orthographic camera looking straight down it.
            float a = Mathf.Tau * i / Shards + 0.31f;
            _dirs[i] = new Vector3(Mathf.Cos(a) + _drift,
                                   (Mathf.Sin(a) * 0.8f + 0.25f) * _flatten,
                                   0f).Normalized();

            var piece = new MeshInstance3D { Mesh = mesh, MaterialOverride = _mat };
            AddChild(piece);
            _pieces[i] = piece;
        }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        float k = _t / _life;
        if (k >= 1f) { QueueFree(); return; }

        // Fast out, slow to a stop: a linear spray reads as a decal moving,
        // an eased one reads as something having been hit.
        float eased = 1f - Mathf.Pow(1f - k, 3f);

        for (int i = 0; i < _pieces.Length; i++)
        {
            _pieces[i].Position = _dirs[i] * eased * _reach;
            float s = Mathf.Max(0.05f, 1f - k);
            _pieces[i].Scale = new Vector3(s, s, s);
        }

        var c = _mat.AlbedoColor;
        c.A = 1f - k;
        _mat.AlbedoColor = c;
        _mat.EmissionEnergyMultiplier = 2.4f * (1f - k);
    }
}
